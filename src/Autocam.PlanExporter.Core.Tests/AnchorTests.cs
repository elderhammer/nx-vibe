using System.Linq;
using Autocam.Plan.Core.Dto;
using Autocam.PlanExporter.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanExporter.Core.Tests
{
    public class AnchorTests
    {
        // 性质：§4.5 + 后置条件 8——feature.anchor_point = 首张关联面质心，模型局部坐标、mm 口径。
        // 依据：plan-exporter.md §4.5（anchor_point 兜底锚点）/ §3.3-8
        // 失败含义：锚点错误会使云端 STEP 侧回填 face_ids 时匹配到错误的面（几何映射风险的根源）。
        [Fact]
        public void Feature_anchor_is_first_face_centroid()
        {
            var plan = PlanFixtures.ExportDefault();

            var cavityWs = plan.Workingsteps.Single(w => w.OperationRef == PlanFixtures.OpByName(plan, "CAVITY_1").OperationId);
            var cavityFeature = plan.Features.Single(f => f.FeatureId == cavityWs.FeatureRef);
            Assert.Equal(new[] { 10.0, 20.0, 5.0 }, cavityFeature.GeometryRef.AnchorPoint);

            var drillWs = plan.Workingsteps.Single(w => w.OperationRef == PlanFixtures.OpByName(plan, "DRILL_1").OperationId);
            var drillFeature = plan.Features.Single(f => f.FeatureId == drillWs.FeatureRef);
            Assert.Equal(new[] { 30.0, 20.0, 0.0 }, drillFeature.GeometryRef.AnchorPoint);
        }

        // 性质：§4.5 / I5——对称碰撞：两张面属性元组（质心+面积+类型+法向）在 0.01 容差内
        //       相同 → warning 提示人工复核（对称/阵列面风险，nx-plugin-design.md §6）。
        // 依据：plan-exporter.md §4.5 / nx-plugin-design.md §6 几何映射风险
        // 失败含义：对称面静默命中错面，导出 plan 的几何引用带病。
        [Fact]
        public void Symmetric_faces_yield_collision_warning()
        {
            var setup = PlanFixtures.DefaultSetup();
            var faceA = setup.Faces[0];
            setup.Faces.Add(new FaceSnapshot
            {
                Tag = "faceC",
                Centroid = faceA.Centroid,
                Area = faceA.Area,
                FaceType = faceA.FaceType,
                Normal = faceA.Normal,
            });
            var op3 = NewDrillOp(setup, "DRILL_2", "faceC");
            setup.ProgramRoot.Children[0].Operations.Add(op3);

            var plan = PlanFixtures.Export(setup);

            Assert.Contains(plan.Diagnostics, d => d.Level == "WARNING" && d.Code == "ANCHOR_COLLISION");
        }

        // 性质：§4.5 / I5 容差边界——质心距离 > 0.01 判为不同面，≤ 0.01 判碰撞（其余属性相同）。
        // 依据：plan-exporter.md §4.5（容差 0.01mm）/ §3.4 I5
        // 失败含义：容差口经漂移会漏报对称碰撞（>）或误报相邻面（<）。
        [Fact]
        public void Collision_tolerance_boundary_is_0_01()
        {
            var beyond = PlanFixtures.DefaultSetup();
            var faceA = beyond.Faces[0];
            beyond.Faces.Add(new FaceSnapshot
            {
                Tag = "faceFar",
                Centroid = new[] { faceA.Centroid[0] + 0.011, faceA.Centroid[1], faceA.Centroid[2] },
                Area = faceA.Area,
                FaceType = faceA.FaceType,
                Normal = faceA.Normal,
            });
            beyond.ProgramRoot.Children[0].Operations.Add(NewDrillOp(beyond, "DRILL_2", "faceFar"));
            Assert.DoesNotContain(PlanFixtures.Export(beyond).Diagnostics,
                d => d.Code == "ANCHOR_COLLISION");

            var within = PlanFixtures.DefaultSetup();
            var faceA2 = within.Faces[0];
            within.Faces.Add(new FaceSnapshot
            {
                Tag = "faceNear",
                Centroid = new[] { faceA2.Centroid[0] + 0.009, faceA2.Centroid[1], faceA2.Centroid[2] },
                Area = faceA2.Area,
                FaceType = faceA2.FaceType,
                Normal = faceA2.Normal,
            });
            within.ProgramRoot.Children[0].Operations.Add(NewDrillOp(within, "DRILL_2", "faceNear"));
            Assert.Contains(PlanFixtures.Export(within).Diagnostics,
                d => d.Level == "WARNING" && d.Code == "ANCHOR_COLLISION");
        }

        // 性质：§4.5——工序几何 Tag 无法匹配任何面 → geometry_ref 省略（schema 可选）+ warning；
        //       其余工序条目不受影响（I7）。
        // 依据：plan-exporter.md §4.5 / §3.4 I7
        // 失败含义：无面数据时伪造锚点会引入错误匹配；整道工序失败会损失其余可导信息。
        [Fact]
        public void Unmatched_geometry_tag_omits_geometry_ref_with_warning()
        {
            var setup = PlanFixtures.DefaultSetup();
            var drillOp = setup.ProgramRoot.Children[0].Operations[1];
            drillOp.GeometryTags.Clear();
            drillOp.GeometryTags.Add("ghost");   // 不在 setup.Faces / setup.Edges

            var plan = PlanFixtures.Export(setup);

            var drillWs = plan.Workingsteps.Single(w => w.OperationRef == PlanFixtures.OpByName(plan, "DRILL_1").OperationId);
            var drillFeature = plan.Features.Single(f => f.FeatureId == drillWs.FeatureRef);
            Assert.Null(drillFeature.GeometryRef);
            Assert.Contains(plan.Diagnostics, d =>
                d.Level == "WARNING" && d.Code == "GEOM_TAG_UNRESOLVED" && d.Detail.Contains("ghost"));

            // 其余工序锚点完好
            var cavityWs = plan.Workingsteps.Single(w => w.OperationRef == PlanFixtures.OpByName(plan, "CAVITY_1").OperationId);
            Assert.NotNull(plan.Features.Single(f => f.FeatureId == cavityWs.FeatureRef).GeometryRef);
        }

        private static OperationSnapshot NewDrillOp(CamSetupSnapshot setup, string name, string tag)
        {
            var pg1 = setup.ProgramRoot.Children[0];
            return new OperationSnapshot
            {
                Name = name,
                TypeName = "DRILL",
                MethodGroup = setup.MethodGroups[1],
                ToolGroup = setup.ToolGroups[1],
                GeometryGroup = setup.GeometryGroups[0],
                ProgramGroup = pg1,
                GeometryTags = { tag },
            };
        }
    }
}
