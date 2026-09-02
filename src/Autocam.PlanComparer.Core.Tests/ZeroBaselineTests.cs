using System.Linq;
using Autocam.PlanComparer.Core.Compare;
using Autocam.PlanComparer.Core.Tests.TestDoubles;
using Autocam.PlanExecutor.Core.Build;
using Autocam.PlanExporter.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanComparer.Core.Tests
{
    public class ZeroBaselineTests
    {
        // 性质：§3.9 零基线（元性质，闭环预演）——全能力 round-trip：Compare(plan, Export(Simulate(Build(plan)))) ≡ 零偏差。
        // 依据：plan-comparer.md §3.9
        // 失败含义：三模块不自洽——Core 自身就会产生偏差，闭环第③步失去零参考点。
        [Fact]
        public void Full_capability_round_trip_is_zero_deviation()
        {
            var plan = PlanFixtures.ExportDefault();
            var rebuilt = PlanFixtures.Export(PlanExecutorPipeline.Build(plan, PlanFixtures.FullCapability()).Simulated);

            var report = PlanComparePipeline.Compare(plan, rebuilt, ComparerFixtures.Context());

            Assert.Empty(report.Deviations);
            Assert.Equal(1.0, report.Scores.StructureConsistency);
            Assert.Equal(0.0, report.Scores.ParamDeviationMean);
            Assert.Equal(1.0, report.Scores.GeometryMatchRate);
        }

        // 性质：§3.9——受限能力 round-trip：仅 known_skip 行（info），无 deviation/missing。
        // 依据：plan-comparer.md §3.9 / §3.8
        // 失败含义：能力跳过的参数污染偏差报告 → 幽灵偏差。
        [Fact]
        public void Restricted_capability_round_trip_has_only_known_skips()
        {
            var plan = PlanFixtures.ExportDefault();
            var profile = PlanFixtures.FullCapability();
            profile.UnsupportedParams.Add("depth_per_cut");   // 导出默认夹具的策略字段（方法组继承值）

            var rebuilt = PlanFixtures.Export(PlanExecutorPipeline.Build(plan, profile).Simulated);

            var report = PlanComparePipeline.Compare(plan, rebuilt, ComparerFixtures.WithUnsupported("depth_per_cut"));

            Assert.Empty(ComparerFixtures.Rows(report, "strategy", "deviation"));
            Assert.Empty(ComparerFixtures.Rows(report, "strategy", "missing"));
            var skip = ComparerFixtures.Rows(report, "strategy", "known_skip").Single();
            Assert.Equal("depth_per_cut", skip.Field);
            Assert.Equal("INFO", skip.Severity);
        }
    }
}
