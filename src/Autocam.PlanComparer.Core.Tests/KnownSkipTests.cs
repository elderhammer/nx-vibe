using System.Linq;
using Autocam.PlanComparer.Core.Compare;
using Autocam.PlanComparer.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanComparer.Core.Tests
{
    public class KnownSkipTests
    {
        // 性质：§3.8 已知跳过 ≠ 偏差（D6）——right 缺字段 ∈ UnsupportedParams → known_skip（info），
        //       不计入偏差计数。
        // 依据：plan-comparer.md §3.8
        // 失败含义：能力跳过的参数被报成偏差 → 幽灵偏差使闭环失真。
        [Fact]
        public void Capability_skipped_field_is_known_skip_not_extra()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Operations[0].Technology.Remove("spindle_rpm");

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.WithUnsupported("spindle_rpm"));

            var row = ComparerFixtures.Rows(report, "parameter", "known_skip").Single();
            Assert.Equal("spindle_rpm", row.Field);
            Assert.Equal("INFO", row.Severity);
            Assert.Empty(ComparerFixtures.Rows(report, "parameter", "extra"));
            Assert.Empty(ComparerFixtures.Rows(report, "parameter", "deviation"));
            Assert.Equal(1, report.Summary.Parameter.KnownSkips);
            Assert.Equal(0, report.Summary.Parameter.Deviations);
        }

        // 性质：§3.8——无豁免（能力画像不含该字段）→ 正常 missing 偏差（right 缺，按偏差计）。
        // 依据：plan-comparer.md §3.8
        // 失败含义：跳过豁免扩大化 → 真实缺失被洗白。
        [Fact]
        public void Missing_field_without_capability_is_missing_deviation()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Operations[0].Technology.Remove("spindle_rpm");

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            Assert.Single(ComparerFixtures.Rows(report, "parameter", "missing"));
            Assert.Equal("WARNING", ComparerFixtures.Rows(report, "parameter", "missing").Single().Severity);
            Assert.Equal(0, report.Summary.Parameter.KnownSkips);
        }

        // 性质：§3.8——豁免仅对顶层字段生效，复合对象内层缺键不豁免（按 missing 报，不洗白）。
        // 依据：plan-comparer.md §4.5（known_skip 仅顶层）
        // 失败含义：内层缺键被误豁免 → 复合参数差异被洗白。
        [Fact]
        public void Nested_missing_key_is_not_skipped()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Operations[0].Strategy["depth"] = new System.Collections.Generic.Dictionary<string, object> { { "value", 25.0 } };   // 缺 mode

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.WithUnsupported("depth"));

            Assert.Single(ComparerFixtures.Rows(report, "strategy", "missing"));
            Assert.Empty(ComparerFixtures.Rows(report, "strategy", "known_skip"));
        }
    }
}
