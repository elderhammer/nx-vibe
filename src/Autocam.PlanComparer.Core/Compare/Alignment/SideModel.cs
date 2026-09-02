using System;
using System.Collections.Generic;
using Autocam.Plan.Core.Diagnostics;
using Autocam.Plan.Core.Plan;
using Autocam.PlanComparer.Core.Report;

namespace Autocam.PlanComparer.Core.Compare.Alignment
{
    /// <summary>workplan 前序叶子：workingstep → operation → feature 引用链解析结果。</summary>
    public sealed class LeafEntry
    {
        public WorkplanNodeEntry Node { get; set; }
        public WorkingstepEntry Ws { get; set; }
        public OperationEntry Op { get; set; }
        public FeatureEntry Feature { get; set; }
    }

    /// <summary>
    /// 一侧 plan 的对齐视图（plan-comparer.md §2.2/§4.2）：前序叶子投影（悬空引用 →
    /// unaligned 行 + error 诊断，剔除出对齐，镜像执行器 §3.4-4 逐条目处置）+
    /// 引用索引（重复 ID 保留首个 + error 诊断）。只读输入，不修改 plan。
    /// </summary>
    public sealed class SideModel
    {
        public PlanRoot Plan { get; set; }

        /// <summary>workplan 前序叶子（不含悬空叶子——已被剔除成行）。</summary>
        public List<LeafEntry> Leaves { get; } = new List<LeafEntry>();

        public Dictionary<string, OperationEntry> OpById { get; } = new Dictionary<string, OperationEntry>();
        public Dictionary<string, WorkingstepEntry> WsById { get; } = new Dictionary<string, WorkingstepEntry>();
        public Dictionary<string, ToolEntry> ToolById { get; } = new Dictionary<string, ToolEntry>();
        public Dictionary<string, SetupEntry> SetupById { get; } = new Dictionary<string, SetupEntry>();
        public Dictionary<string, FeatureEntry> FeatureById { get; } = new Dictionary<string, FeatureEntry>();

        public static SideModel Build(PlanRoot plan, string sideName, List<DeviationEntry> rows, DiagnosticsCollector diag)
        {
            var model = new SideModel { Plan = plan };
            Index(model.OpById, plan.Operations, o => o.OperationId, "operation_id", sideName, diag);
            Index(model.WsById, plan.Workingsteps, w => w.WorkingstepId, "workingstep_id", sideName, diag);
            Index(model.ToolById, plan.Resources.Tools, t => t.ToolId, "tool_id", sideName, diag);
            Index(model.SetupById, plan.Setups, s => s.SetupId, "setup_id", sideName, diag);
            Index(model.FeatureById, plan.Features, f => f.FeatureId, "feature_id", sideName, diag);

            CollectLeaves(plan.Workplan.Root, model, sideName, rows, diag);
            return model;
        }

        private static void Index<T>(Dictionary<string, T> map, IEnumerable<T> items, Func<T, string> key, string keyName, string sideName, DiagnosticsCollector diag)
        {
            foreach (var item in items)
            {
                var k = key(item);
                if (map.ContainsKey(k))
                {
                    diag.Error("DUPLICATE_ID",
                        string.Format("对比 {0} 侧 {1} 重复：{2}，保留首个（plan-comparer.md §3.10-4）", sideName, keyName, k));
                }
                else
                {
                    map[k] = item;
                }
            }
        }

        private static void CollectLeaves(WorkplanNodeEntry node, SideModel model, string sideName, List<DeviationEntry> rows, DiagnosticsCollector diag)
        {
            if (!string.IsNullOrEmpty(node.WorkingstepRef))
            {
                // 叶子：沿 workingstep → operation → feature 解析，任一环悬空 → unaligned 行 + error，剔除
                if (!model.WsById.TryGetValue(node.WorkingstepRef, out var ws))
                {
                    Dangling(node.WorkingstepRef, node.WorkingstepRef, sideName, rows, diag);
                    return;
                }
                if (!model.OpById.TryGetValue(ws.OperationRef, out var op))
                {
                    Dangling(ws.OperationRef, node.WorkingstepRef, sideName, rows, diag);
                    return;
                }
                if (!model.FeatureById.TryGetValue(ws.FeatureRef, out var feature))
                {
                    Dangling(ws.FeatureRef, node.WorkingstepRef, sideName, rows, diag);
                    return;
                }
                model.Leaves.Add(new LeafEntry { Node = node, Ws = ws, Op = op, Feature = feature });
                return;
            }

            // 组节点：递归子节点（前序 = 刀路输出序，§4.2）
            foreach (var child in node.Children)
            {
                CollectLeaves(child, model, sideName, rows, diag);
            }
        }

        private static void Dangling(string danglingRef, string workingstepRef, string sideName, List<DeviationEntry> rows, DiagnosticsCollector diag)
        {
            var detail = string.Format("{0} 侧 workplan 叶子 workingstep_ref={1} 的引用 {2} 悬空，无法对齐（plan-comparer.md §3.10-4）",
                sideName, workingstepRef, danglingRef);
            rows.Add(new DeviationEntry
            {
                Dimension = ReportDimensions.Structure,
                Kind = DeviationKinds.Unaligned,
                Severity = DiagnosticsCollector.LevelError,
                Field = danglingRef,
                Detail = detail,
            });
            diag.Error("REFERENCE_DANGLING", detail);
        }
    }
}
