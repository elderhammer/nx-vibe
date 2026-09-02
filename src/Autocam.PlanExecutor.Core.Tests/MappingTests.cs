using System.Linq;
using Autocam.PlanExecutor.Core.Build;
using Autocam.PlanExecutor.Core.Tests.TestDoubles;
using Autocam.PlanExporter.Core.Export;
using Autocam.PlanExporter.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanExecutor.Core.Tests
{
    public class MappingTests
    {
        // 性质：§4.3——operation_type → typeName 反向表（规范 typeName，first-wins 口径，
        //       bore→BORING、probe→ON_MACHINE_PROBING）。
        // 依据：plan-executor.md §4.3 / nxopen-research.md §4.2
        // 失败含义：反向映射错位 → 重建工序类型与 plan 意图不符。
        [Theory]
        [InlineData("mill_cavity", "CAVITY_MILL", "MILLING")]
        [InlineData("mill_zlevel", "ZLEVEL_PROFILE", "MILLING")]
        [InlineData("drill", "DRILL", "DRILLING")]
        [InlineData("drill_peck", "PECK_DRILLING", "DRILLING")]
        [InlineData("bore", "BORING", "DRILLING")]
        [InlineData("probe", "ON_MACHINE_PROBING", "PROBING")]
        [InlineData("wedm", "WEDM_OPERATION", "WEDM")]
        [InlineData("machine_control", "MILL_MACHINE_CONTROL", "MACHINE_CONTROL")]
        public void Operation_type_maps_to_canonical_typename(string operationType, string expectedTypeName, string expectedDomain)
        {
            Assert.True(TypeMapper.TryMapOperationType(operationType, out var typeName, out var domain));
            Assert.Equal(expectedTypeName, typeName);
            Assert.Equal(expectedDomain, domain);
        }

        // 性质：§4.3——反向映射落到命令层：工序命令 TypeName 为规范 typeName。
        // 依据：plan-executor.md §4.3
        // 失败含义：映射表正确但未接入管线。
        [Fact]
        public void Command_typename_uses_reverse_mapping()
        {
            var result = PlanExecutorPipeline.Build(ExecutorFixtures.FullPlan(), PlanFixtures.FullCapability());

            var ops = result.Commands.OfType<CreateOperationCommand>().ToArray();
            Assert.Equal("CAVITY_MILL", ops.Single(o => o.Name == "CAVITY_1").TypeName);
            Assert.Equal("DRILL", ops.Single(o => o.Name == "DRILL_1").TypeName);
        }

        // 性质：§4.3——"other" + nx_template.type 非空 → 按原始 typeName 直落（近似工序，
        //       nx-plugin-design.md §6），方法组按 UNKNOWN 域约定建。
        // 依据：plan-executor.md §4.3 / §4.2 命名表
        // 失败含义：近似工序失去按真实类型落地的依据。
        [Fact]
        public void Other_type_uses_nx_template_typename()
        {
            var plan = ExecutorFixtures.FullPlan();
            plan.Operations[0].OperationType = "other";
            plan.Operations[0].NxTemplate.Type = "SOME_FUTURE_OP";

            var result = PlanExecutorPipeline.Build(plan, PlanFixtures.FullCapability());

            var cavity = result.Commands.OfType<CreateOperationCommand>().Single(o => o.Name == "CAVITY_1");
            Assert.Equal("SOME_FUTURE_OP", cavity.TypeName);
            Assert.Equal("METHOD", cavity.MethodGroupName);   // UNKNOWN 域约定名
        }

        // 性质：§4.3——SetParam 按 ParamRegistry 顺序输出（确定性骨架的一部分）。
        // 依据：plan-executor.md §4.3 / dev-pattern.md 确定性纪律
        // 失败含义：参数顺序漂移破坏命令序列字节级确定性。
        [Fact]
        public void Set_params_follow_registry_order()
        {
            var result = PlanExecutorPipeline.Build(ExecutorFixtures.FullPlan(), PlanFixtures.FullCapability());

            var cavity = result.Commands.OfType<CreateOperationCommand>().Single(o => o.Name == "CAVITY_1");
            var names = cavity.Params.Select(p => p.Name).ToArray();
            Assert.Equal(new[]
            {
                "cut_pattern", "depth_per_cut", "stepover", "floor_stock", "wall_stock",
                "cross_over_distance",            // strategy
                "spindle_rpm", "feed_cut", "coolant",   // technology
            }, names);
        }
    }
}
