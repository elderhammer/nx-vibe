using System;
using System.Collections.Generic;
using NXOpen.CAM;

namespace Autocam.Nx.Adapter.Export
{
    /// <summary>
    /// 操作类型名解析（M1 实测：Operation 无 TypeName 属性；GetNameOfType() 给 UI 标签）。
    /// 依据 = CreateBuilder(op) 返回的 Builder 运行时类型名 → NX typeName 表（TypeMapper 输入）。
    /// 未入表 → "other"（TypeMapper 兜底，原始信息保留 + 调用侧 warning）。
    /// </summary>
    public static class TypeNameResolver
    {
        private static readonly Dictionary<string, string> BuilderToTypeName =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "FacingBuilder", "FACE_MILLING" },
                { "FacingZigZagBuilder", "FACE_MILLING" },
                { "FaceMillingBuilder", "FACE_MILLING" },
                { "EdgeChamferBuilder", "CHAMFER_MILL" },
                { "CavityMillingBuilder", "CAVITY_MILL" },
                { "PlanarMillingBuilder", "PLANAR_MILL" },
                { "ZLevelMillingBuilder", "ZLEVEL_PROFILE" },
                { "SurfaceContourBuilder", "SURFACE_CONTOUR" },
                { "GrooveMillingBuilder", "GROOVE_MILL" },
                { "ChamferMillingBuilder", "CHAMFER_MILL" },
                { "PlungeMillingBuilder", "PLUNGE_MILL" },
                { "CylinderMillingBuilder", "CYLINDER_MILL" },
                { "EngravingBuilder", "ENGRAVE" },
                { "HoleDrillingBuilder", "DRILL" },
                { "ThreadMillingBuilder", "THREAD_MILLING" },
                { "PointToPointBuilder", "SPOT_DRILLING" },
                { "DocumentationBuilder", "DOCUMENTATION" },
                { "MillMachineControlBuilder", "MILL_MACHINE_CONTROL" },
                { "MillUserDefinedBuilder", "MILL_USER_DEFINED" },
                { "FeatureMillingBuilder", "FEATURE_MILLING" },
                { "PlanarRoughingBuilder", "PLANAR_ROUGHING" },
                { "WallMillingBuilder", "WALL_MILLING" },
            };

        public static string Resolve(OperationBuilder builder)
        {
            if (builder != null && BuilderToTypeName.TryGetValue(builder.GetType().Name, out var typeName))
            {
                return typeName;
            }
            return "other";
        }

        /// <summary>builder 运行时类型名（未入表时保留进诊断，绝不静默）。</summary>
        public static string BuilderTypeName(OperationBuilder builder)
        {
            return builder == null ? "<null>" : builder.GetType().Name;
        }
    }
}
