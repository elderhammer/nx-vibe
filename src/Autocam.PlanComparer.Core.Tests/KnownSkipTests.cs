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

        // 性质：NX 写保护豁免（合同增强）——right 缺字段且 (工序类型, 字段) 命中
        //       UnwritableByPlanType → known_skip（info），不计偏差（绝不静默：判定必须命中结构化表）。
        // 依据：plan-comparer.md §3.8 + NxWriteProtection 实测表（M3_Probe E 段）
        // 失败含义：NX 写保护字段被报成偏差 → 对抗 NX 语义制造幽灵偏差。
        [Fact]
        public void Nx_write_protected_field_is_known_skip()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Operations[0].Strategy.Remove("cycle");

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.WithUnwritable("drill", "cycle"));

            var row = ComparerFixtures.Rows(report, "strategy", "known_skip").Single();
            Assert.Equal("cycle", row.Field);
            Assert.Equal("INFO", row.Severity);
            Assert.Empty(ComparerFixtures.Rows(report, "strategy", "missing"));
            Assert.Equal(0, report.Summary.Strategy.Deviations);
        }

        // 性质：豁免粒度纪律——字段命中但工序类型不匹配 → 仍按 missing 报（表是 (类型,字段) 双键）。
        // 依据：CapabilityProfile.UnwritableByPlanType 语义
        // 失败含义：豁免扩大化 → 其它类型工序的真实缺失被洗白。
        [Fact]
        public void Write_protection_does_not_apply_to_other_plan_type()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Operations[0].Strategy.Remove("cycle");

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.WithUnwritable("mill_cavity", "cycle"));

            Assert.Single(ComparerFixtures.Rows(report, "strategy", "missing"));
            Assert.Empty(ComparerFixtures.Rows(report, "strategy", "known_skip"));
        }

        // 性质：豁免字段纪律——类型命中但字段未入表 → 仍按 missing 报（不越界）。
        // 依据：CapabilityProfile.UnwritableByPlanType 语义
        // 失败含义：同类型下未入表字段被豁免 → 真实缺失被洗白。
        [Fact]
        public void Write_protection_does_not_apply_to_other_field()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Operations[0].Strategy.Remove("cycle");

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.WithUnwritable("drill", "depth"));

            Assert.Single(ComparerFixtures.Rows(report, "strategy", "missing"));
            Assert.Empty(ComparerFixtures.Rows(report, "strategy", "known_skip"));
        }

        // 性质：§3.1 自反性在豁免表注入下不破——Compare(P,P) 零偏差（两侧全字段，豁免不触发）。
        // 依据：plan-comparer.md §3.1 + §3.8
        // 失败含义：豁免逻辑在两侧一致时也产生行 → 比较器基本性质被破坏。
        [Fact]
        public void Reflexivity_holds_with_write_protection_profile()
        {
            var plan = ComparerFixtures.OneOpPlan();
            var report = PlanComparePipeline.Compare(plan, plan, ComparerFixtures.WithUnwritable("drill", "cycle", "depth"));

            Assert.Empty(report.Deviations);
            Assert.Equal(0, report.Summary.Strategy.KnownSkips);
        }

        // 性质：写保护豁免覆盖「两侧都有值」路径——重建侧模板默认被显式化（M3 实测
        //       floor_stock 0 vs 0.2），值比较必偏差 → 命中 (类型,字段) 表 → known_skip，跳过值比较。
        // 依据：NxWriteProtection 实测（M3_Probe E1：新建 op 模板默认 InheritanceStatus=False）
        // 失败含义：写保护字段因「两侧都有值」漏出豁免 → 对抗 NX 语义的幽灵偏差。
        [Fact]
        public void Write_protected_field_with_both_values_is_known_skip()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Operations[0].Strategy["cycle"] = "OTHER";   // 两侧都有值但不同

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.WithUnwritable("drill", "cycle"));

            var row = ComparerFixtures.Rows(report, "strategy", "known_skip").Single();
            Assert.Equal("cycle", row.Field);
            Assert.Equal("INFO", row.Severity);
            Assert.Empty(ComparerFixtures.Rows(report, "strategy", "deviation"));
            Assert.Equal(0, report.Summary.Strategy.Deviations);
        }
    }
}
