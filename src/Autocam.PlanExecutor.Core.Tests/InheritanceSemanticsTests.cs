using System.Linq;
using Autocam.PlanExecutor.Core.Build;
using Autocam.PlanExecutor.Core.Tests.TestDoubles;
using Autocam.PlanExporter.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanExecutor.Core.Tests
{
    public class InheritanceSemanticsTests
    {
        // 性质：§3.3——缺字段不产生 Set 命令：工序参数集 == plan 出现字段 ∩ ParamRegistry，
        //       绝不伪造值（导入侧缺省字段由 NX 继承组/模板默认，nx-plugin-design.md §5 注释）。
        // 依据：plan-executor.md §3.3
        // 失败含义：伪造默认值会掩盖 ground truth 与重建结果的差异；多下发则覆盖继承语义。
        [Fact]
        public void Missing_plan_fields_produce_no_set_params()
        {
            var result = PlanExecutorPipeline.Build(ExecutorFixtures.SparseDrillPlan(), PlanFixtures.FullCapability());

            var op = result.Commands.OfType<CreateOperationCommand>().Single();
            Assert.Equal(new[] { "cycle" }, op.Params.Select(p => p.Name).ToArray());
            Assert.Equal("PECK", op.Params.Single().Value);
        }

        // 性质：§3.3——参数值直通不换算（镜像导出侧 I6：mm/rpm 口径）。
        // 依据：plan-executor.md §3.3 / plan-exporter.md §3.4 I6
        // 失败含义：任何换算都会让重建参数与 plan 出现系统偏差。
        [Fact]
        public void Param_values_pass_through_unconverted()
        {
            var result = PlanExecutorPipeline.Build(ExecutorFixtures.FullPlan(), PlanFixtures.FullCapability());

            var cavity = result.Commands.OfType<CreateOperationCommand>().Single(o => o.Name == "CAVITY_1");
            var values = cavity.Params.ToDictionary(p => p.Name, p => p.Value);
            Assert.Equal(2.0, (double)values["depth_per_cut"]);                 // mm
            Assert.Equal(6000.0, System.Convert.ToDouble(values["spindle_rpm"])); // rpm
            Assert.Equal(0.3, (double)values["floor_stock"]);                   // mm
        }
    }
}
