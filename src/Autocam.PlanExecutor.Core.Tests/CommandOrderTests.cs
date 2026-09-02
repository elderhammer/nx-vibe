using System.Linq;
using Autocam.PlanExecutor.Core.Build;
using Autocam.PlanExecutor.Core.Tests.TestDoubles;
using Autocam.PlanExporter.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanExecutor.Core.Tests
{
    public class CommandOrderTests
    {
        // 性质：§3.2——命令序 = 规范顺序：CamSetup → 方法组 → 刀具组 → 几何组 →
        //       Program 组（前序）→ 工序（workplan 叶子序）。
        // 依据：plan-executor.md §2.2 规范顺序 / §3.2
        // 失败含义：命令乱序 → NX 侧建组失败或刀路输出顺序与 plan 不一致。
        [Fact]
        public void Commands_follow_canonical_order()
        {
            var plan = ExecutorFixtures.FullPlan();
            var result = PlanExecutorPipeline.Build(plan, PlanFixtures.FullCapability());

            // ID 为不透明字符串（格式不作合同），期望值从 plan 读取而非硬编码
            var setupId = plan.Setups[0].SetupId;
            var toolIds = plan.Resources.Tools.Select(t => t.ToolId).ToArray();
            var actual = result.Commands.Select(Describe).ToArray();
            var expected = new[]
            {
                "CREATE_CAM_SETUP",
                "CREATE_METHOD_GROUP:MILL_ROUGH",
                "CREATE_METHOD_GROUP:DRILL_METHOD",
                "CREATE_TOOL_GROUP:" + toolIds[0],
                "CREATE_TOOL_GROUP:" + toolIds[1],
                "CREATE_GEOMETRY_GROUP:" + setupId,
                "CREATE_PROGRAM_GROUP:PROGRAM",
                "CREATE_PROGRAM_GROUP:PROGRAM_1",
                "CREATE_OPERATION:CAVITY_1",
                "CREATE_OPERATION:DRILL_1",
            };
            Assert.Equal(expected, actual);
        }

        // 性质：§3.2——嵌套 workplan：父组先于子组（ParentName 链正确）。
        // 依据：plan-executor.md §3.2
        // 失败含义：父组未建即挂子组，NX 侧创建失败。
        [Fact]
        public void Nested_program_groups_parent_before_child()
        {
            var result = PlanExecutorPipeline.Build(ExecutorFixtures.NestedWorkplanPlan(), PlanFixtures.FullCapability());

            var programs = result.Commands.OfType<CreateProgramGroupCommand>().ToArray();
            Assert.Equal(new[] { "PROGRAM", "SUB", "PROGRAM_1" }, programs.Select(p => p.Name).ToArray());
            Assert.Null(programs[0].ParentName);
            Assert.Equal("PROGRAM", programs[1].ParentName);
            Assert.Equal("SUB", programs[2].ParentName);
        }

        // 性质：§4.2——方法组按加工域约定名（首次出现序）。
        // 依据：plan-executor.md §4.2 命名表
        // 失败含义：方法组域错配 → 工序继承错误的方法组默认值。
        [Fact]
        public void Method_groups_follow_domain_first_appearance()
        {
            var result = PlanExecutorPipeline.Build(ExecutorFixtures.TwoOpPlan(), PlanFixtures.FullCapability());

            var methods = result.Commands.OfType<CreateMethodGroupCommand>().Select(m => m.Name).ToArray();
            Assert.Equal(new[] { "DRILL_METHOD", "MILL_ROUGH" }, methods);   // 钻孔在前
        }

        private static string Describe(RebuildCommand command)
        {
            switch (command)
            {
                case CreateCamSetupCommand _: return "CREATE_CAM_SETUP";
                case CreateMethodGroupCommand m: return "CREATE_METHOD_GROUP:" + m.Name;
                case CreateToolGroupCommand t: return "CREATE_TOOL_GROUP:" + t.Name;
                case CreateGeometryGroupCommand g: return "CREATE_GEOMETRY_GROUP:" + g.Name;
                case CreateProgramGroupCommand p: return "CREATE_PROGRAM_GROUP:" + p.Name;
                case CreateOperationCommand o: return "CREATE_OPERATION:" + o.Name;
                default: return command.Kind;
            }
        }
    }
}
