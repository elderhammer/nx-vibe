using System.Linq;
using Autocam.PlanExecutor.Core.Build;
using Autocam.PlanExecutor.Core.Tests.TestDoubles;
using Autocam.PlanExporter.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanExecutor.Core.Tests
{
    public class GroupTreeRebuildTests
    {
        // 性质：§3.2——setups → Geometry 组命令，MCS/安全平面/夹具偏置直填。
        // 依据：plan-executor.md §2.2 / nxopen-research.md §4.7
        // 失败含义：装夹数据错位 → 重建工程 MCS 与 ground truth 不一致。
        [Fact]
        public void Geometry_group_carries_mcs_values()
        {
            var plan = ExecutorFixtures.FullPlan();
            var result = PlanExecutorPipeline.Build(plan, PlanFixtures.FullCapability());

            var geom = result.Commands.OfType<CreateGeometryGroupCommand>().Single();
            Assert.Equal(plan.Setups[0].SetupId, geom.Name);   // ID 不透明，从 plan 读取
            Assert.Equal(new[] { 0.0, 0.0, 0.0 }, geom.Origin);
            Assert.Equal(new[] { 0.0, 0.0, 1.0 }, geom.ZAxis);
            Assert.Equal(new[] { 1.0, 0.0, 0.0 }, geom.XAxis);
            Assert.Equal(50.0, (double)geom.SafePlaneZ);
            Assert.Equal(1, (int)geom.FixtureOffset);
        }

        // 性质：§3.2——tools → Tool 组命令，MVP 刀具字段直填（name = tool_id）。
        // 依据：plan-executor.md §2.2 / nx-plugin-design.md §5
        // 失败含义：刀具参数丢失 → 重建刀路与 ground truth 刀具不符。
        [Fact]
        public void Tool_group_carries_tool_fields()
        {
            var plan = ExecutorFixtures.FullPlan();
            var result = PlanExecutorPipeline.Build(plan, PlanFixtures.FullCapability());

            var tools = result.Commands.OfType<CreateToolGroupCommand>().ToArray();
            Assert.Equal(plan.Resources.Tools.Select(t => t.ToolId).ToArray(), tools.Select(t => t.Name).ToArray());
            Assert.Equal("END_MILL", tools[0].Params["type"]);
            Assert.Equal(10.0, (double)tools[0].Params["diameter"]);
            Assert.Equal(4, (int)tools[0].Params["num_flutes"]);
            Assert.Equal(25.0, (double)tools[0].Params["flute_length"]);
            Assert.Equal(0.0, (double)tools[0].Params["lower_corner_radius"]);
            Assert.Equal("DRILL", tools[1].Params["type"]);
        }

        // 性质：§3.2——工序命令四父组名齐全（program/method/tool/geometry 各归其位）。
        // 依据：plan-executor.md §2.2 / nxopen-research.md §3.2（OperationCollection.Create 四父组）
        // 失败含义：父组错挂 → NX 侧工序落在错误组下，四视图对比全错。
        [Fact]
        public void Operation_command_carries_four_parent_groups()
        {
            var plan = ExecutorFixtures.FullPlan();
            var result = PlanExecutorPipeline.Build(plan, PlanFixtures.FullCapability());

            var ops = result.Commands.OfType<CreateOperationCommand>().ToArray();
            var cavity = ops.Single(o => o.Name == "CAVITY_1");
            Assert.Equal("PROGRAM_1", cavity.ProgramGroupName);
            Assert.Equal("MILL_ROUGH", cavity.MethodGroupName);   // 约定名是合同的一部分
            Assert.Equal(plan.Resources.Tools[0].ToolId, cavity.ToolGroupName);
            Assert.Equal(plan.Setups[0].SetupId, cavity.GeometryGroupName);

            var drill = ops.Single(o => o.Name == "DRILL_1");
            Assert.Equal("DRILL_METHOD", drill.MethodGroupName);
            Assert.Equal(plan.Resources.Tools[1].ToolId, drill.ToolGroupName);
        }
    }
}
