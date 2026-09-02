using System.Linq;
using Autocam.PlanExecutor.Core.Build;
using Autocam.PlanExecutor.Core.Tests.TestDoubles;
using Autocam.PlanExporter.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanExecutor.Core.Tests
{
    public class DiagnosticsContractTests
    {
        private static readonly string[] Levels = { "INFO", "WARNING", "ERROR" };

        // 性质：§3.5-3——所有跳过/降级行为均有诊断条目（绝不静默省略），
        //       且条目形状 {level ∈ INFO/WARNING/ERROR, code, detail} 合法。
        // 依据：plan-executor.md §3.5-3
        // 失败含义：跳过行为无迹可查时，重建结果的缺口无法被闭环报告定位。
        [Fact]
        public void All_skips_have_diagnostics_with_valid_shape()
        {
            var plan = ExecutorFixtures.FullPlan();
            plan.Operations[0].ToolRef = "T-999";                    // 悬空引用 → error + 跳过
            plan.Operations[1].Strategy["bottom_clearance"] = 2.0;   // 能力不支持 → warning + 跳参数
            var profile = PlanFixtures.FullCapability();
            profile.UnsupportedParams.Add("bottom_clearance");

            var result = PlanExecutorPipeline.Build(plan, profile);

            Assert.Single(result.Commands.OfType<CreateOperationCommand>());   // 只剩 DRILL_1
            Assert.Contains(result.Diagnostics, d => d.Level == "ERROR" && d.Code == "REFERENCE_DANGLING");
            Assert.Contains(result.Diagnostics, d => d.Level == "WARNING" && d.Code == "CAPABILITY_UNSUPPORTED");
            foreach (var d in result.Diagnostics)
            {
                Assert.Contains(d.Level, Levels);
                Assert.False(string.IsNullOrEmpty(d.Code));
                Assert.False(string.IsNullOrEmpty(d.Detail));
            }
        }

        // 性质：健康输入无 ERROR 级诊断——ERROR 只属于真实的失败路径（镜像导出侧）。
        // 依据：plan-executor.md §3.4（健康输入满足全部前置条件）
        // 失败含义：健康 plan 被误报，闭环会误判合同有问题。
        [Fact]
        public void Healthy_plan_builds_without_errors()
        {
            var result = PlanExecutorPipeline.Build(ExecutorFixtures.FullPlan(), PlanFixtures.FullCapability());

            Assert.DoesNotContain(result.Diagnostics, d => d.Level == "ERROR");
        }
    }
}
