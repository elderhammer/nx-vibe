using System;
using System.Collections.Generic;
using System.Linq;
using Autocam.Plan.Core.Diagnostics;
using Autocam.Plan.Core.Plan;
using Autocam.PlanComparer.Core.Report;

namespace Autocam.PlanComparer.Core.Compare.Alignment
{
    /// <summary>
    /// workplan 组树对比（plan-comparer.md §4.3）：位置 + 组名双键递归。
    /// 同位置异名 → deviation 行（field=组名，两侧组名落 left/right）；子树多/缺 →
    /// missing（right 缺）/ extra（right 多）组行。组级行 operation_ref 为空，
    /// 与工序行同入 structure 维度。
    /// </summary>
    public static class WorkplanTreeDiffer
    {
        public static void Diff(WorkplanNodeEntry leftRoot, WorkplanNodeEntry rightRoot, List<DeviationEntry> rows)
        {
            DiffRec(leftRoot, rightRoot, rows);
        }

        private static void DiffRec(WorkplanNodeEntry left, WorkplanNodeEntry right, List<DeviationEntry> rows)
        {
            if (left.Name != right.Name)
            {
                rows.Add(new DeviationEntry
                {
                    Dimension = ReportDimensions.Structure,
                    Kind = DeviationKinds.Deviation,
                    Severity = DiagnosticsCollector.LevelWarning,
                    Field = left.Name,
                    Left = left.Name,
                    Right = right.Name,
                    Detail = string.Format("workplan 组名不同：{0} vs {1}（plan-comparer.md §4.3）", left.Name, right.Name),
                });
            }

            // 叶子节点属对齐器（§4.2），组树只比较组节点——避免同一工序在结构维度双重报行
            var leftGroups = left.Children.Where(c => string.IsNullOrEmpty(c.WorkingstepRef)).ToList();
            var rightGroups = right.Children.Where(c => string.IsNullOrEmpty(c.WorkingstepRef)).ToList();
            var min = Math.Min(leftGroups.Count, rightGroups.Count);
            for (var i = 0; i < min; i++)
            {
                DiffRec(leftGroups[i], rightGroups[i], rows);
            }
            for (var i = min; i < leftGroups.Count; i++)
            {
                rows.Add(new DeviationEntry
                {
                    Dimension = ReportDimensions.Structure,
                    Kind = DeviationKinds.Missing,
                    Severity = DiagnosticsCollector.LevelWarning,
                    Field = leftGroups[i].Name,
                    Left = leftGroups[i].Name,
                    Detail = string.Format("right 侧缺 workplan 组 {0}（plan-comparer.md §4.3）", leftGroups[i].Name),
                });
            }
            for (var i = min; i < rightGroups.Count; i++)
            {
                rows.Add(new DeviationEntry
                {
                    Dimension = ReportDimensions.Structure,
                    Kind = DeviationKinds.Extra,
                    Severity = DiagnosticsCollector.LevelWarning,
                    Field = rightGroups[i].Name,
                    Right = rightGroups[i].Name,
                    Detail = string.Format("right 侧多出 workplan 组 {0}（plan-comparer.md §4.3）", rightGroups[i].Name),
                });
            }
        }
    }
}
