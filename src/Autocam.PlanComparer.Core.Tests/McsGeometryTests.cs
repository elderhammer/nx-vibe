using System.Linq;
using Autocam.Plan.Core.Plan;
using Autocam.PlanComparer.Core.Compare;
using Autocam.PlanComparer.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanComparer.Core.Tests
{
    public class McsGeometryTests
    {
        // 性质：§4.5 MCS 维度——原点向量欧氏距离超 0.01 → deviation（setup 级行，operation_ref 空）。
        // 依据：plan-comparer.md §4.5
        // 失败含义：MCS 平移被吞 → 重建坐标系偏移导致刀路整体错位。
        [Fact]
        public void Origin_deviation_is_reported()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Setups[0].Mcs.Origin = new[] { 0.5, 0.0, 0.0 };

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            var row = ComparerFixtures.Rows(report, "mcs", "deviation").Single();
            Assert.Equal("origin", row.Field);
            Assert.Null(row.OperationRef);
            Assert.Equal(0.5, (double)row.Delta, 5);
        }

        // 性质：§4.5——Z 轴方向不同 → deviation；安全平面/夹具偏置按各自口径。
        // 依据：plan-comparer.md §4.5 / §3.4
        // 失败含义：主轴方向/夹具号差异漏报 → 装夹错误。
        [Fact]
        public void Axis_safe_plane_and_fixture_offset_deviations_are_reported()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Setups[0].Mcs.ZAxis = new[] { 0.0, 1.0, 0.0 };
            right.Setups[0].SafePlaneZ = 50.5;
            right.Setups[0].FixtureOffset = 2;

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            Assert.Single(ComparerFixtures.Rows(report, "mcs", "deviation"), r => r.Field == "z_axis");
            Assert.Single(ComparerFixtures.Rows(report, "mcs", "deviation"), r => r.Field == "safe_plane_z");
            Assert.Single(ComparerFixtures.Rows(report, "mcs", "deviation"), r => r.Field == "fixture_offset");
        }

        // 性质：§4.5——安全平面 0.005 差在容差内 → 一致。
        // 依据：plan-comparer.md §3.4（AbsoluteMm 0.01）
        // 失败含义：微小浮点差被放大为偏差行，报告噪声淹没真问题。
        [Fact]
        public void Safe_plane_within_tolerance_matches()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Setups[0].SafePlaneZ = 50.005;

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            Assert.Empty(ComparerFixtures.Rows(report, "mcs"));
        }

        // 性质：§4.5——setup 数不同 → missing/extra 行；单侧 mcs 缺失 → missing 行（field=mcs）。
        // 依据：plan-comparer.md §4.5
        // 失败含义：装夹结构差异不可见。
        [Fact]
        public void Setup_count_and_mcs_presence_are_reported()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Setups[0].Mcs = null;                                    // right 缺 mcs
            right.Setups.Add(new SetupEntry { SetupId = "SET-2" });        // right 多出 setup

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            Assert.Single(ComparerFixtures.Rows(report, "mcs", "missing"), r => r.Field == "mcs");
            Assert.Single(ComparerFixtures.Rows(report, "mcs", "extra"));
        }

        // 性质：§4.5 几何维度——锚点距离超 0.01 → deviation；评分几何匹配率下降。
        // 依据：plan-comparer.md §4.5 / §4.6
        // 失败含义：关联几何错位漏报 → FaceResolver 兜底失效。
        [Fact]
        public void Anchor_deviation_is_reported_and_scores_geometry()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Features[0].GeometryRef.AnchorPoint = new[] { 10.0, 20.0, 6.0 };

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            var row = ComparerFixtures.Rows(report, "geometry", "deviation").Single();
            Assert.Equal("anchor_point", row.Field);
            Assert.Equal(0.0, report.Scores.GeometryMatchRate);
        }

        // 性质：§4.5——对称碰撞：左锚点 0.01 内命中多个右锚点 → warning 诊断（镜像导出器 §4.5）。
        // 依据：plan-comparer.md §4.5
        // 失败含义：对称/阵列面碰撞静默 → 错误匹配无人复核。
        [Fact]
        public void Symmetric_anchor_collision_warns()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            // right 追加一个未引用特征，锚点与 F-1 相同（对称碰撞）
            right.Features.Add(new FeatureEntry
            {
                FeatureId = "F-9",
                FeatureType = "hole",
                GeometryRef = new GeometryRefEntry { AnchorPoint = new[] { 10.0, 20.0, 5.0 } },
            });

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            Assert.Contains(report.Diagnostics, d => d.Level == "WARNING" && d.Code == "GEOMETRY_AMBIGUOUS");
            Assert.Equal(1, report.Summary.Geometry.Collisions);
        }
    }
}
