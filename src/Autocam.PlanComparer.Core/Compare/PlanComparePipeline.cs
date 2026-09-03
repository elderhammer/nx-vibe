using System.Collections.Generic;
using Autocam.Plan.Core.Diagnostics;
using Autocam.Plan.Core.Dto;
using Autocam.Plan.Core.Plan;
using Autocam.PlanComparer.Core.Compare.Alignment;
using Autocam.PlanComparer.Core.Compare.Dimensions;
using Autocam.PlanComparer.Core.Compare.Scoring;
using Autocam.PlanComparer.Core.Report;

namespace Autocam.PlanComparer.Core.Compare
{
    /// <summary>
    /// PlanComparer 门面（类名不叫 PlanComparer 以避开与命名空间 Autocam.PlanComparer
    /// 的 C# 简单名冲突——dev-pattern.md 反面清单 #1），plan-comparer.md §4.1 总流程：
    /// 1. 前置检查（§3.10-1/2，致命抛 CompareAbortedException）
    /// 2. 侧模型（叶子投影 + 引用闭合，§4.2）
    /// 3. 结构：组树对比（§4.3）+ 工序对齐（§4.2）
    /// 4. 七维度对比（§4.5；toolpath 预留不比较）
    /// 5. 评分聚合（§4.6）+ 报告组装
    /// 纯函数：输入两个 plan + 上下文 → ComparisonReport；同输入必同输出（确定性 §3.6）。
    /// </summary>
    public static class PlanComparePipeline
    {
        public static ComparisonReport Compare(PlanRoot left, PlanRoot right, CompareContext context)
        {
            // 1. 前置检查（§3.10-1/2）
            if (left == null)
            {
                throw new CompareAbortedException("前置条件 1 不满足：left plan 为 null（plan-comparer.md §3.10-1）");
            }
            if (right == null)
            {
                throw new CompareAbortedException("前置条件 1 不满足：right plan 为 null（plan-comparer.md §3.10-1）");
            }
            if (left.Workplan == null || left.Workplan.Root == null)
            {
                throw new CompareAbortedException("前置条件 2 不满足：left workplan.root 缺失，对齐无权威序（plan-comparer.md §3.10-2）");
            }
            if (right.Workplan == null || right.Workplan.Root == null)
            {
                throw new CompareAbortedException("前置条件 2 不满足：right workplan.root 缺失，对齐无权威序（plan-comparer.md §3.10-2）");
            }
            var capability = (context ?? new CompareContext()).RightCapability ?? new CapabilityProfile();

            var rows = new List<DeviationEntry>();
            var diag = new DiagnosticsCollector();
            var tally = new CompareTally();

            // 2. 侧模型（叶子投影 + 引用闭合；悬空 → unaligned 行 + error，剔除出对齐）
            var leftSide = SideModel.Build(left, "left", rows, diag);
            var rightSide = SideModel.Build(right, "right", rows, diag);

            // 3. 结构维度：组树对比 + 工序对齐（行序 §3.11-3：structure 先行）
            WorkplanTreeDiffer.Diff(left.Workplan.Root, right.Workplan.Root, rows);
            var pairs = OpAligner.Align(leftSide, rightSide, rows, diag, tally);

            // 4. 七维度（固定序：tool → parameter → strategy → mcs → geometry；toolpath 预留不比较）
            ToolComparer.Compare(pairs, leftSide, rightSide, rows, diag, tally);
            foreach (var pair in pairs)
            {
                ParamDictComparer.CompareDicts(
                    pair.Left.Op.Technology, pair.Right.Op.Technology,
                    ReportDimensions.Parameter, pair.Left.Op.OperationId, pair.Left.Op.OperationType,
                    capability, rows, diag, tally);
                ParamDictComparer.CompareDicts(
                    pair.Left.Op.Strategy, pair.Right.Op.Strategy,
                    ReportDimensions.Strategy, pair.Left.Op.OperationId, pair.Left.Op.OperationType,
                    capability, rows, diag, tally);
            }
            McsComparer.Compare(leftSide, rightSide, rows, diag, tally);
            GeometryComparer.Compare(pairs, leftSide, rightSide, rows, diag, tally);

            // 5. 评分聚合 + 报告组装（§4.6/§4.7）
            var report = new ComparisonReport
            {
                ReportId = "cmp-" + left.PlanId + "-" + right.PlanId,
                Left = new ReportSide { PlanId = left.PlanId, Name = left.Name, InputRef = left.InputRef },
                Right = new ReportSide { PlanId = right.PlanId, Name = right.Name, InputRef = right.InputRef },
            };
            report.Deviations.AddRange(rows);
            ScoringAggregator.Build(rows, tally, report.Summary, report.Scores);
            report.Diagnostics.AddRange(diag.Entries);
            return report;
        }
    }
}
