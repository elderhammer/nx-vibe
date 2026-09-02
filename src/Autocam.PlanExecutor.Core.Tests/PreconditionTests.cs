using System.Linq;
using Autocam.PlanExecutor.Core.Build;
using Autocam.PlanExecutor.Core.Tests.TestDoubles;
using Autocam.Plan.Core.Plan;
using Autocam.PlanExporter.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanExecutor.Core.Tests
{
    public class PreconditionTests
    {
        // 性质：§3.4-1——plan 为 null → BuildAbortedException，终止。
        // 依据：plan-executor.md §3.4-1
        // 失败含义：空输入被当成空计划，产出无意义工程。
        [Fact]
        public void Null_plan_aborts()
        {
            Assert.Throws<BuildAbortedException>(
                () => PlanExecutorPipeline.Build(null, PlanFixtures.FullCapability()));
        }

        // 性质：§3.4-2——operations 为空 → BuildAbortedException（镜像导出侧前置 2）。
        // 依据：plan-executor.md §3.4-2
        // 失败含义：空 plan 静默重建出空工程，闭环失真。
        [Fact]
        public void Empty_operations_abort()
        {
            var plan = new PlanRoot
            {
                PlanId = "PLAN-E",
                Workplan = new WorkplanEntry { Root = new WorkplanNodeEntry { Name = "PROGRAM" } },
            };

            Assert.Throws<BuildAbortedException>(
                () => PlanExecutorPipeline.Build(plan, PlanFixtures.FullCapability()));
        }

        // 性质：§3.4-3——workplan.root 缺失 → BuildAbortedException（schema 必填，防御性）。
        // 依据：plan-executor.md §3.4-3
        // 失败含义：无组树依据时工序无处挂载。
        [Fact]
        public void Missing_workplan_root_aborts()
        {
            var plan = ExecutorFixtures.SparseDrillPlan();
            plan.Workplan = new WorkplanEntry();

            Assert.Throws<BuildAbortedException>(
                () => PlanExecutorPipeline.Build(plan, PlanFixtures.FullCapability()));
        }

        // 性质：§3.4-4——tool_ref 悬空 → error + 跳过该工序，其余照常（逐条目处置，不整体失败）。
        // 依据：plan-executor.md §3.4-4
        // 失败含义：整体失败损失其余可重建内容；静默继续则产出自带悬空引用的工程。
        [Fact]
        public void Dangling_tool_ref_skips_that_operation()
        {
            var plan = ExecutorFixtures.FullPlan();
            plan.Operations[0].ToolRef = "T-999";

            var result = PlanExecutorPipeline.Build(plan, PlanFixtures.FullCapability());

            var ops = result.Commands.OfType<CreateOperationCommand>().ToArray();
            Assert.Equal(new[] { "DRILL_1" }, ops.Select(o => o.Name).ToArray());
            Assert.Contains(result.Diagnostics, d =>
                d.Level == "ERROR" && d.Code == "REFERENCE_DANGLING" && d.Detail.Contains("T-999"));
        }

        // 性质：§3.4-4——workplan 叶子的 workingstep_ref 悬空 → error + 跳过该叶子，其余照常。
        // 依据：plan-executor.md §3.4-4
        // 失败含义：悬空叶子被当作合法工序，或拖垮整棵组树。
        [Fact]
        public void Dangling_workplan_leaf_is_skipped()
        {
            var plan = ExecutorFixtures.FullPlan();
            var leaf = plan.Workplan.Root.Children[0].Children[0];   // CAVITY_1 叶子
            leaf.WorkingstepRef = "WS-999";

            var result = PlanExecutorPipeline.Build(plan, PlanFixtures.FullCapability());

            var ops = result.Commands.OfType<CreateOperationCommand>().ToArray();
            Assert.Equal(new[] { "DRILL_1" }, ops.Select(o => o.Name).ToArray());
            Assert.Contains(result.Diagnostics, d =>
                d.Level == "ERROR" && d.Code == "REFERENCE_DANGLING" && d.Detail.Contains("WS-999"));
        }

        // 性质：§3.4-5——operation_type="other" 且 nx_template.type 空 → error + 跳过
        //       （无 typeName 无法建工序，近似工序口径）。
        // 依据：plan-executor.md §3.4-5 / nx-plugin-design.md §6
        // 失败含义：静默跳过会丢工序；猜测类型会建错工序。
        [Fact]
        public void Other_without_template_type_is_skipped()
        {
            var plan = ExecutorFixtures.FullPlan();
            plan.Operations[0].OperationType = "other";
            plan.Operations[0].NxTemplate.Type = "";

            var result = PlanExecutorPipeline.Build(plan, PlanFixtures.FullCapability());

            var ops = result.Commands.OfType<CreateOperationCommand>().ToArray();
            Assert.Equal(new[] { "DRILL_1" }, ops.Select(o => o.Name).ToArray());
            Assert.Contains(result.Diagnostics, d =>
                d.Level == "ERROR" && d.Code == "OPERATION_TYPE_UNMAPPABLE");
        }
    }
}
