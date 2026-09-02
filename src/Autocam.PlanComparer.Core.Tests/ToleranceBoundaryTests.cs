using System.Collections.Generic;
using System.Linq;
using Autocam.PlanComparer.Core.Compare;
using Autocam.PlanComparer.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanComparer.Core.Tests
{
    public class ToleranceBoundaryTests
    {
        // 性质：§3.4 容差边界——压线（|Δ| = 0.01）算一致（含入语义）。
        //       边界值取双精度可精确表示/精确运算的常数（0.0 vs 0.01），
        //       避免 25.01 - 25.0 = 0.010000000000001563 这类表示误差翻转判定。
        // 依据：plan-comparer.md §3.4
        // 失败含义：边界语义漂移（开/闭区间不定）→ 同一工程重复对比结论摆动。
        [Fact]
        public void Absolute_delta_at_tolerance_is_a_match()
        {
            var left = ComparerFixtures.OneOpPlan();
            left.Operations[0].Technology["bottom_stock"] = 0.0;
            var right = ComparerFixtures.OneOpPlan();
            right.Operations[0].Technology["bottom_stock"] = 0.01;

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            Assert.Empty(report.Deviations);
        }

        // 性质：§3.4——越线（|Δ| > 0.01）→ deviation 行，delta/tolerance 落行（0.0 vs 0.02 精确）。
        // 依据：plan-comparer.md §3.4
        // 失败含义：超差被吞 → 线性尺寸偏差无法量化。
        [Fact]
        public void Absolute_delta_above_tolerance_is_a_deviation()
        {
            var left = ComparerFixtures.OneOpPlan();
            left.Operations[0].Technology["bottom_stock"] = 0.0;
            var right = ComparerFixtures.OneOpPlan();
            right.Operations[0].Technology["bottom_stock"] = 0.02;

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            var row = ComparerFixtures.Rows(report, "parameter", "deviation").Single();
            Assert.Equal("bottom_stock", row.Field);
            Assert.Equal(0.02, (double)row.Delta);
            Assert.Equal(0.01, (double)row.Tolerance);
        }

        // 性质：§3.4——相对口径（转速/进给 5%）：压线 1140/1200（r = 60/1200 = 5%，精确）算一致，
        //       1139（61/1200 > 5%）越线。
        // 依据：plan-comparer.md §3.4
        // 失败含义：转速进给按绝对容差判定 → 大值误报小值漏报。
        [Fact]
        public void Relative_tolerance_uses_five_percent_boundary()
        {
            var left = ComparerFixtures.OneOpPlan();
            var atLimit = ComparerFixtures.OneOpPlan();
            atLimit.Operations[0].Technology["spindle_rpm"] = 1140;   // 60/1200 = 5% = 容差
            var overLimit = ComparerFixtures.OneOpPlan();
            overLimit.Operations[0].Technology["spindle_rpm"] = 1139; // 61/1200 = 5.08% > 5%

            var reportAt = PlanComparePipeline.Compare(left, atLimit, ComparerFixtures.Context());
            var reportOver = PlanComparePipeline.Compare(left, overLimit, ComparerFixtures.Context());

            Assert.Empty(ComparerFixtures.Rows(reportAt, "parameter"));
            Assert.Single(ComparerFixtures.Rows(reportOver, "parameter", "deviation"));
        }

        // 性质：§3.4——相对口径双零（L=R=0）→ 一致，r=0，不除零。
        // 依据：plan-comparer.md §2.3（r 定义：双零 → 0）
        // 失败含义：双零除零 → NaN 污染评分与报告。
        [Fact]
        public void Relative_tolerance_with_both_zero_matches()
        {
            var left = ComparerFixtures.OneOpPlan();
            left.Operations[0].Technology["spindle_rpm"] = 0;
            var right = ComparerFixtures.OneOpPlan();
            right.Operations[0].Technology["spindle_rpm"] = 0;

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            Assert.Empty(report.Deviations);
            Assert.Equal(0.0, report.Scores.ParamDeviationMean);
        }

        // 性质：§3.4——向量欧氏距离：0.005 内一致、0.02 越线（边界语义由标量压线用例覆盖）。
        // 依据：plan-comparer.md §3.4
        // 失败含义：MCS/锚点按分量比较而非欧氏距离 → 对角偏差漏报。
        [Fact]
        public void Vector_distance_within_and_beyond_tolerance()
        {
            var left = ComparerFixtures.OneOpPlan();
            var near = ComparerFixtures.OneOpPlan();
            near.Features[0].GeometryRef.AnchorPoint = new[] { 10.0, 20.0, 5.005 };
            var far = ComparerFixtures.OneOpPlan();
            far.Features[0].GeometryRef.AnchorPoint = new[] { 10.0, 20.0, 5.02 };

            var reportNear = PlanComparePipeline.Compare(left, near, ComparerFixtures.Context());
            var reportFar = PlanComparePipeline.Compare(left, far, ComparerFixtures.Context());

            Assert.Empty(ComparerFixtures.Rows(reportNear, "geometry"));
            var row = ComparerFixtures.Rows(reportFar, "geometry", "deviation").Single();
            Assert.Equal("anchor_point", row.Field);
            Assert.Equal(0.02, (double)row.Delta, 5);
            Assert.Equal(0.01, (double)row.Tolerance);
        }
    }
}
