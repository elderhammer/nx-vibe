using System;
using System.Linq;
using Autocam.PlanComparer.Core.Compare;
using Autocam.PlanComparer.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanComparer.Core.Tests
{
    public class ScoringTests
    {
        // 性质：§4.6——param_deviation_mean = 全部容差口径数值字段相对偏差的均值（一致字段 r=0 也计入）。
        //       单字段偏差时均值 = 该字段 r / 字段总数（可精确复算）。
        // 依据：plan-comparer.md §4.6 / §2.3
        // 失败含义：评分公式漂移 → 汇总分数无法复算，报告页展示值不可信。
        [Fact]
        public void Param_deviation_mean_is_recomputable()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Operations[0].Technology["spindle_rpm"] = 1300;

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            // OneOpPlan 容差口径数值字段（配对且双侧存在）：depth.value(25)、spindle_rpm、feed_cut.value(120)、diameter(6.8)
            // 各 r = 0、spindle_rpm r = 100/1300；safe_plane_z 属 mcs 维度不计入
            const double expected = (100.0 / 1300.0) / 4.0;
            Assert.Equal(expected, report.Scores.ParamDeviationMean, 10);
            Assert.Equal(1.0, report.Scores.StructureConsistency);
            Assert.Equal(1.0, report.Scores.GeometryMatchRate);
        }

        // 性质：§4.6——summary 计数与偏差行一一可核对（按 dimension+kind 重算相等）。
        // 依据：plan-comparer.md §4.6
        // 失败含义：汇总与明细不一致 → 报告页计数与表格对不上。
        [Fact]
        public void Summary_counts_match_deviation_rows()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Resources.Tools[0].Diameter = 7.0;                        // tool deviation
            right.Operations[0].Technology["spindle_rpm"] = 1300;           // parameter deviation
            right.Operations[0].Strategy["cycle"] = "DRILL";                // strategy deviation
            right.Features[0].GeometryRef.AnchorPoint = new[] { 10.0, 20.0, 6.0 }; // geometry deviation

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            Assert.Equal(Count(report, "tool", "deviation"), report.Summary.Tool.Deviations);
            Assert.Equal(Count(report, "parameter", "deviation"), report.Summary.Parameter.Deviations);
            Assert.Equal(Count(report, "strategy", "deviation"), report.Summary.Strategy.Deviations);
            Assert.Equal(Count(report, "geometry", "deviation"), report.Summary.Geometry.Deviations);
            Assert.Equal(Count(report, "structure", "missing"), report.Summary.Structure.Missing);
            Assert.Equal(Count(report, "structure", "extra"), report.Summary.Structure.Extra);
        }

        // 性质：I2 评分单调——修复偏差（把字段改回一致）→ param_deviation_mean 不升（严格降）。
        // 依据：plan-comparer.md §3.12-I2
        // 失败含义：评分与偏差脱钩 → 「改好了分更低」的反直觉结果。
        [Fact]
        public void Fixing_a_deviation_never_raises_scores_mean()
        {
            var left = ComparerFixtures.OneOpPlan();
            var bad = ComparerFixtures.OneOpPlan();
            bad.Operations[0].Technology["spindle_rpm"] = 1300;

            var reportBad = PlanComparePipeline.Compare(left, bad, ComparerFixtures.Context());
            var reportGood = PlanComparePipeline.Compare(left, left, ComparerFixtures.Context());

            Assert.True(reportBad.Scores.ParamDeviationMean > 0);
            Assert.Equal(0.0, reportGood.Scores.ParamDeviationMean);
            Assert.True(reportGood.Scores.ParamDeviationMean < reportBad.Scores.ParamDeviationMean);
        }

        private static int Count(Autocam.PlanComparer.Core.Report.ComparisonReport report, string dimension, string kind)
        {
            return report.Deviations.Count(r => r.Dimension == dimension && r.Kind == kind);
        }
    }
}
