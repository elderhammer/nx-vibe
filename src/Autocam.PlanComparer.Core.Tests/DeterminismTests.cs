using System.Linq;
using Autocam.PlanComparer.Core.Compare;
using Autocam.PlanComparer.Core.Report;
using Autocam.PlanComparer.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanComparer.Core.Tests
{
    public class DeterminismTests
    {
        // 性质：§3.6 确定性——同输入两次 Compare → 报告字节级相同。
        // 依据：plan-comparer.md §3.6
        // 失败含义：存在不确定源（字典序/随机/时间）→ 报告无法回归复现。
        [Fact]
        public void Same_inputs_produce_byte_identical_reports()
        {
            var left = ComparerFixtures.TwoOpPlan();
            var right = ComparerFixtures.TwoOpPlan();
            right.Operations[1].Technology["spindle_rpm"] = 7000;

            var first = ReportSerializer.Serialize(PlanComparePipeline.Compare(left, right, ComparerFixtures.Context()));
            var second = ReportSerializer.Serialize(PlanComparePipeline.Compare(left, right, ComparerFixtures.Context()));

            Assert.Equal(first, second);
        }

        // 性质：§3.11-3 行序确定——维度固定序 structure→tool→parameter→strategy→mcs→geometry。
        // 依据：plan-comparer.md §3.11-3
        // 失败含义：行序随实现抖动 → 报告页渲染与逐条核对的基线不稳。
        [Fact]
        public void Row_order_follows_fixed_dimension_sequence()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Resources.Tools[0].Diameter = 7.0;                        // tool
            right.Operations[0].Strategy["cycle"] = "DRILL";                // strategy

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            Assert.Equal(new[] { "tool", "strategy" }, report.Deviations.Select(r => r.Dimension).ToArray());
        }
    }
}
