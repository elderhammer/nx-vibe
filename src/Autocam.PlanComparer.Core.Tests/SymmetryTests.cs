using System.Linq;
using Autocam.PlanComparer.Core.Compare;
using Autocam.PlanComparer.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanComparer.Core.Tests
{
    public class SymmetryTests
    {
        // 性质：§3.2 对称性——数值偏差互换方向：delta 取负、|delta| 与评分不变。
        // 依据：plan-comparer.md §3.2
        // 失败含义：口径偏向一侧，左右互换后结论漂移，报告无法被复核。
        [Fact]
        public void Numeric_deviation_flips_sign_when_sides_swap()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Operations[0].Strategy["depth"] = new System.Collections.Generic.Dictionary<string, object> { { "mode", "THROUGH" }, { "value", 26.0 } };

            var ab = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());
            var ba = PlanComparePipeline.Compare(right, left, ComparerFixtures.Context());

            var rowAb = ab.Deviations.Single(r => r.Field == "depth.value");
            var rowBa = ba.Deviations.Single(r => r.Field == "depth.value");
            Assert.Equal(1.0, (double)rowAb.Delta);
            Assert.Equal(-1.0, (double)rowBa.Delta);
            Assert.Equal(rowAb.Tolerance, rowBa.Tolerance);
            Assert.Equal(ab.Scores.StructureConsistency, ba.Scores.StructureConsistency);
            Assert.Equal(ab.Scores.ParamDeviationMean, ba.Scores.ParamDeviationMean);
            Assert.Equal(ab.Scores.GeometryMatchRate, ba.Scores.GeometryMatchRate);
        }

        // 性质：§3.2——missing ↔ extra 互换、匹配数与评分不变。
        // 依据：plan-comparer.md §3.2
        // 失败含义：缺/多口径不对称，方向性错误会误导「重建丢了什么 vs 多造了什么」。
        [Fact]
        public void Missing_and_extra_swap_when_sides_swap()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Operations[0].Strategy["cut_pattern"] = "FOLLOW_PART";   // right 多出

            var ab = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());
            var ba = PlanComparePipeline.Compare(right, left, ComparerFixtures.Context());

            Assert.Single(ComparerFixtures.Rows(ab, "strategy", "extra"));
            Assert.Single(ComparerFixtures.Rows(ba, "strategy", "missing"));
            Assert.Equal(ab.Scores.StructureConsistency, ba.Scores.StructureConsistency);
            Assert.Equal(ab.Scores.ParamDeviationMean, ba.Scores.ParamDeviationMean);
        }

        // 性质：§3.2——结构对称：一侧工序多/缺，missing ↔ extra 互换、评分不变（分母取 max）。
        // 依据：plan-comparer.md §3.2 / §2.3
        // 失败含义：结构一致率按单侧分母，互换后分数漂移。
        [Fact]
        public void Structure_missing_extra_swap_keeps_scores()
        {
            var left = ComparerFixtures.TwoOpPlan();
            var right = ComparerFixtures.TwoOpPlan();
            // 删除 right 的第二个工序（mill）：叶子、工步、工序、特征、刀具一并移除
            right.Workplan.Root.Children[0].Children.RemoveAt(1);
            right.Operations.RemoveAt(1);
            right.Workingsteps.RemoveAt(1);
            right.Features.RemoveAt(1);
            right.Resources.Tools.RemoveAt(1);

            var ab = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());
            var ba = PlanComparePipeline.Compare(right, left, ComparerFixtures.Context());

            Assert.Single(ComparerFixtures.Rows(ab, "structure", "missing"));
            Assert.Single(ComparerFixtures.Rows(ba, "structure", "extra"));
            Assert.Equal(0.5, ab.Scores.StructureConsistency);
            Assert.Equal(ab.Scores.StructureConsistency, ba.Scores.StructureConsistency);
            Assert.Equal(ab.Scores.GeometryMatchRate, ba.Scores.GeometryMatchRate);
        }
    }
}
