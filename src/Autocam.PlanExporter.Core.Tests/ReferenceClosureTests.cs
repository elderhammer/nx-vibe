using System.Collections.Generic;
using System.Linq;
using Autocam.Plan.Core.Dto;
using Autocam.Plan.Core.Plan;
using Autocam.PlanExporter.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanExporter.Core.Tests
{
    public class ReferenceClosureTests
    {
        // 性质：后置条件 4（§3.3-4）——plan 内所有 *_ref 均指向 plan 内实体，无悬空引用。
        // 依据：plan-exporter.md §3.3-4
        // 失败含义：PlanExecutor 加载 plan 时引用解析失败，重建中断。
        [Fact]
        public void Default_export_has_closed_references()
        {
            var plan = PlanFixtures.ExportDefault();

            AssertClosedReferences(plan);
        }

        // 性质：前置条件 6 处置——刀具组不在 ToolGroups 表内的工序报 error 并跳过，
        //       输出仍满足引用闭合（"闭合"是校验过的事实，而不是跳过坏工序的巧合）。
        // 依据：plan-exporter.md §3.2-6 / §3.3-4
        // 失败含义：组装出指向不存在工具的 plan，或跳过行为破坏了其余条目的完整性。
        [Fact]
        public void Op_with_orphan_tool_group_is_skipped_with_error()
        {
            var setup = PlanFixtures.DefaultSetup();
            var drillOp = setup.ProgramRoot.Children[0].Operations[1];
            drillOp.ToolGroup = PlanFixtures.NewGroup(GroupKind.Tool, "T_ORPHAN", "orphan tool");   // 不在 setup.ToolGroups

            var plan = PlanFixtures.Export(setup);

            Assert.Single(plan.Operations);   // CAVITY_1 保留，DRILL_1 跳过
            Assert.Contains(plan.Diagnostics, d =>
                d.Level == "ERROR" && d.Code == "MISSING_PARENT_GROUP" && d.Detail.Contains("DRILL_1"));
            AssertClosedReferences(plan);
        }

        /// <summary>后置条件 4 的检查逻辑（测试侧独立实现，与导出器内部校验互不引用）。</summary>
        private static void AssertClosedReferences(PlanRoot plan)
        {
            var toolIds = new HashSet<string>(plan.Resources.Tools.Select(t => t.ToolId));
            var featureIds = new HashSet<string>(plan.Features.Select(f => f.FeatureId));
            var opIds = new HashSet<string>(plan.Operations.Select(o => o.OperationId));
            var wsIds = new HashSet<string>(plan.Workingsteps.Select(w => w.WorkingstepId));
            var setupIds = new HashSet<string>(plan.Setups.Select(s => s.SetupId));

            foreach (var op in plan.Operations)
            {
                Assert.True(string.IsNullOrEmpty(op.ToolRef) || toolIds.Contains(op.ToolRef),
                    "tool_ref 悬空: " + op.OperationId + " -> " + op.ToolRef);
            }
            foreach (var ws in plan.Workingsteps)
            {
                Assert.True(opIds.Contains(ws.OperationRef), "operation_ref 悬空: " + ws.WorkingstepId);
                Assert.True(featureIds.Contains(ws.FeatureRef), "feature_ref 悬空: " + ws.WorkingstepId);
                Assert.True(setupIds.Contains(ws.SetupRef), "setup_ref 悬空: " + ws.WorkingstepId);
            }
            foreach (var node in plan.Workplan.Elements.Where(e => e.WorkingstepRef != null))
            {
                Assert.True(wsIds.Contains(node.WorkingstepRef), "workplan workingstep_ref 悬空: " + node.Name);
            }
        }
    }
}
