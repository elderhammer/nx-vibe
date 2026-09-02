using System.Collections.Generic;
using System.Linq;
using Autocam.PlanExporter.Core.Dto;
using Autocam.PlanExporter.Core.Export;
using Autocam.PlanExporter.Core.Plan;

namespace Autocam.PlanExporter.Core.Tests.TestDoubles
{
    /// <summary>
    /// 手写内存假对象（不上 mock 框架）：构造合成 NX 工程快照，无任何 NX 依赖。
    /// 默认夹具 = 最小铣+孔工程（nx-plugin-design.md §7）：PROGRAM_1 下
    /// CAVITY_1(CAVITY_MILL) + DRILL_1(DRILL)，方法/刀具/几何组、MCS、两张关联面齐全。
    /// 各测试在默认夹具上做局部变异。
    /// </summary>
    public static class PlanFixtures
    {
        public static CapabilityProfile FullCapability() => new CapabilityProfile();

        public static PlanRoot Export(CamSetupSnapshot setup) => PlanExportPipeline.Export(setup, FullCapability());

        public static PlanRoot Export(CamSetupSnapshot setup, CapabilityProfile profile) => PlanExportPipeline.Export(setup, profile);

        public static PlanRoot ExportDefault() => Export(DefaultSetup());

        public static GroupSnapshot NewGroup(GroupKind kind, string name, string displayName)
        {
            return new GroupSnapshot { Kind = kind, Name = name, DisplayName = displayName };
        }

        public static Dictionary<string, object> Feed(double value, string unit)
        {
            return new Dictionary<string, object> { { "value", value }, { "unit", unit } };
        }

        public static CamSetupSnapshot DefaultSetup()
        {
            var setup = new CamSetupSnapshot { PartName = "demo.prt", InputRef = "demo.step" };

            // Geometry 组：MCS + 安全平面 + 夹具偏置（§4.6）
            var mcs = NewGroup(GroupKind.Geometry, "MCS_1", "MCS 1");
            mcs.Params["origin"] = new double[] { 0.0, 0.0, 0.0 };
            mcs.Params["z_axis"] = new double[] { 0.0, 0.0, 1.0 };
            mcs.Params["x_axis"] = new double[] { 1.0, 0.0, 0.0 };
            mcs.Params["safe_plane_z"] = 50.0;
            mcs.Params["fixture_offset"] = 1;

            // 刀具组（§4.5，最直填的部分）
            var t1 = NewGroup(GroupKind.Tool, "T1_D10", "D10 End Mill");
            t1.Params["type"] = "END_MILL";
            t1.Params["diameter"] = 10.0;
            t1.Params["num_flutes"] = 4;
            t1.Params["flute_length"] = 25.0;
            t1.Params["lower_corner_radius"] = 0.0;
            t1.Params["cross_over_distance"] = 5.0;   // Tool 优先源参数（ParamRegistry.Source）

            var t2 = NewGroup(GroupKind.Tool, "T2_D6.8", "D6.8 Drill");
            t2.Params["type"] = "DRILL";
            t2.Params["diameter"] = 6.8;
            t2.Params["num_flutes"] = 2;

            // 方法组（继承链第一级）
            var rough = NewGroup(GroupKind.Method, "MILL_ROUGH", "Roughing");
            rough.Params["depth_per_cut"] = 2.0;
            rough.Params["cut_pattern"] = "FOLLOW_PART";
            rough.Params["floor_stock"] = 0.5;   // 被 CAVITY_1 操作级显式值 0.3 覆盖（拍平测试用）
            rough.Params["spindle_rpm"] = 6000;
            rough.Params["feed_cut"] = Feed(1200.0, "MMPM");
            rough.Params["coolant"] = "FLOOD";

            var drillMethod = NewGroup(GroupKind.Method, "DRILL_METHOD", "Drilling");
            drillMethod.Params["cycle"] = "PECK";
            drillMethod.Params["spindle_rpm"] = 1200;
            drillMethod.Params["feed_cut"] = Feed(120.0, "MMPM");

            // 关联几何面
            var faceA = new FaceSnapshot
            {
                Tag = "faceA",
                Centroid = new[] { 10.0, 20.0, 5.0 },
                Area = 1000.0,
                FaceType = "Planar",
                Normal = new[] { 0.0, 0.0, 1.0 },
            };
            var faceB = new FaceSnapshot
            {
                Tag = "faceB",
                Centroid = new[] { 30.0, 20.0, 0.0 },
                Area = 36.317,
                FaceType = "Cylindrical",
                Normal = new[] { 0.0, 1.0, 0.0 },
            };

            // 工序
            var cavity = new OperationSnapshot
            {
                Name = "CAVITY_1",
                TypeName = "CAVITY_MILL",
                MethodGroup = rough,
                ToolGroup = t1,
                GeometryGroup = mcs,
                GeometryTags = { "faceA" },
            };
            cavity.Params["floor_stock"] = new OpParam { IsSet = true, Value = 0.3 };
            cavity.Params["wall_stock"] = new OpParam { IsSet = true, Value = 0.3 };
            cavity.Params["stepover"] = new OpParam
            {
                IsSet = true,
                Value = new Dictionary<string, object> { { "mode", "PERCENT" }, { "value", 50 } },
            };

            var drillOp = new OperationSnapshot
            {
                Name = "DRILL_1",
                TypeName = "DRILL",
                MethodGroup = drillMethod,
                ToolGroup = t2,
                GeometryGroup = mcs,
                GeometryTags = { "faceB" },
            };
            drillOp.Params["depth"] = new OpParam
            {
                IsSet = true,
                Value = new Dictionary<string, object> { { "mode", "THROUGH" }, { "value", 25.0 } },
            };
            drillOp.Params["bottom_stock"] = new OpParam { IsSet = true, Value = 0.0 };

            // Program 树：PROGRAM → PROGRAM_1 → [CAVITY_1, DRILL_1]（前序 = 刀路输出序）
            var programRoot = NewGroup(GroupKind.Program, "PROGRAM", "MainProgram");
            var pg1 = NewGroup(GroupKind.Program, "PROGRAM_1", "Setup 1");
            pg1.Operations.Add(cavity);
            pg1.Operations.Add(drillOp);
            programRoot.Children.Add(pg1);
            cavity.ProgramGroup = pg1;
            drillOp.ProgramGroup = pg1;

            setup.ProgramRoot = programRoot;
            setup.MethodGroups.AddRange(new[] { rough, drillMethod });
            setup.ToolGroups.AddRange(new[] { t1, t2 });
            setup.GeometryGroups.Add(mcs);
            setup.Faces.AddRange(new[] { faceA, faceB });
            return setup;
        }

        // ---- 断言辅助 ----

        /// <summary>沿 workplan 叶子名 → workingstep → operation 引用链定位工序条目（顺带验证引用链闭合）。</summary>
        public static OperationEntry OpByName(PlanRoot plan, string opName)
        {
            var leaf = plan.Workplan.Elements.First(e => e.Name == opName);
            var ws = plan.Workingsteps.First(w => w.WorkingstepId == leaf.WorkingstepRef);
            return plan.Operations.First(o => o.OperationId == ws.OperationRef);
        }

        /// <summary>按 code 取诊断条目。</summary>
        public static DiagnosticEntry[] Diag(PlanRoot plan, string code)
        {
            return plan.Diagnostics.Where(d => d.Code == code).ToArray();
        }
    }
}
