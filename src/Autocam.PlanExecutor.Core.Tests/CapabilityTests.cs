using System.Linq;
using Autocam.PlanExecutor.Core.Build;
using Autocam.PlanExecutor.Core.Tests.TestDoubles;
using Autocam.PlanExporter.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanExecutor.Core.Tests
{
    public class CapabilityTests
    {
        // 性质：§3.3——能力探测不支持的参数跳过 + warning（镜像导出侧前置 5），
        //       该工序其余参数照发。
        // 依据：plan-executor.md §3.3 / plan-exporter.md §3.2-5
        // 失败含义：把当前 NX 版本读不懂的参数下发 → 适配层执行失败；整道工序失败则损失其余可重建内容。
        [Fact]
        public void Unsupported_param_is_skipped_with_warning()
        {
            var plan = ExecutorFixtures.FullPlan();
            plan.Operations[1].Strategy["bottom_clearance"] = 2.0;
            var profile = PlanFixtures.FullCapability();
            profile.UnsupportedParams.Add("bottom_clearance");

            var result = PlanExecutorPipeline.Build(plan, profile);

            var drill = result.Commands.OfType<CreateOperationCommand>().Single(o => o.Name == "DRILL_1");
            Assert.DoesNotContain(drill.Params, p => p.Name == "bottom_clearance");
            Assert.Contains(drill.Params, p => p.Name == "cycle");   // 其余参数照发
            Assert.Contains(result.Diagnostics, d =>
                d.Level == "WARNING" && d.Code == "CAPABILITY_UNSUPPORTED" && d.Detail.Contains("bottom_clearance"));
        }
    }
}
