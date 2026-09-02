using System;
using System.Collections.Generic;

namespace Autocam.PlanExporter.Core.Export
{
    /// <summary>
    /// §4.4：NX typeName/subtypeName → plan operation_type 枚举 + 加工域。
    /// 映射表 = nxopen-research.md §4.2 全表（数据与遍历逻辑隔离，扩表不改码）。
    /// 未命中：operation_type = "other"、域 = UNKNOWN（不参与许可检查），
    /// 原始 typeName 由调用侧全量保留进 nx_template，不猜测、不丢弃
    /// （nx-plugin-design.md §6 近似工序口径）。
    /// </summary>
    public static class TypeMapper
    {
        /// <summary>加工域常量（与 CapabilityProfile.UnavailableLicenses 对齐）。</summary>
        public static class Domains
        {
            public const string Milling = "MILLING";
            public const string Drilling = "DRILLING";
            public const string Turning = "TURNING";
            public const string MultiAxis = "MULTI_AXIS";
            public const string Wedm = "WEDM";
            public const string Additive = "ADDITIVE";
            public const string Probing = "PROBING";
            public const string MachineControl = "MACHINE_CONTROL";
            public const string UserDefined = "USER_DEFINED";
            public const string Unknown = "UNKNOWN";
        }

        private static readonly Dictionary<string, Tuple<string, string>> Table =
            new Dictionary<string, Tuple<string, string>>(StringComparer.Ordinal)
            {
                // 铣削 2.5 轴
                { "CAVITY_MILL", T("mill_cavity", Domains.Milling) },
                { "PLANAR_MILL", T("mill_planar", Domains.Milling) },
                { "FACE_MILLING", T("mill_face", Domains.Milling) },
                { "PLUNGE_MILL", T("mill_plunge", Domains.Milling) },
                { "GROOVE_MILL", T("mill_groove", Domains.Milling) },
                // 铣削 3 轴
                { "ZLEVEL_PROFILE", T("mill_zlevel", Domains.Milling) },
                { "ZLEVEL_FOLLOW_PARTS", T("mill_zlevel", Domains.Milling) },
                { "SURFACE_CONTOUR", T("mill_surface", Domains.Milling) },
                { "FLOWCUT", T("mill_flowcut", Domains.Milling) },
                { "CHAMFER_MILL", T("mill_chamfer", Domains.Milling) },
                { "ENGRAVE", T("mill_engrave", Domains.Milling) },
                { "CYLINDER_MILL", T("mill_cylinder", Domains.Milling) },
                // 孔加工
                { "SPOT_DRILLING", T("drill_center", Domains.Drilling) },
                { "DRILL", T("drill", Domains.Drilling) },
                { "DRILL_DEEP", T("drill", Domains.Drilling) },
                { "PECK_DRILLING", T("drill_peck", Domains.Drilling) },
                { "BREAK_CHIP_DRILLING", T("drill_break_chip", Domains.Drilling) },
                { "TAPPING", T("tap", Domains.Drilling) },
                { "THREAD_MILLING", T("thread_mill", Domains.Drilling) },
                { "REAMING", T("ream", Domains.Drilling) },
                { "BORING", T("bore", Domains.Drilling) },
                { "BACK_BORING", T("bore", Domains.Drilling) },
                { "COUNTERBORE", T("counterbore", Domains.Drilling) },
                { "COUNTERSINK", T("countersink", Domains.Drilling) },
                // 车削
                { "ROUGH_TURNING", T("turn_rough", Domains.Turning) },
                { "FINISH_TURNING", T("turn_finish", Domains.Turning) },
                { "THREAD_TURNING", T("turn_thread", Domains.Turning) },
                { "CENTERLINE_DRILL_TURNING", T("turn_drill", Domains.Turning) },
                { "MULTI_AXIS_TURN_MILL", T("turn_mill", Domains.Turning) },
                // 多轴
                { "MULTI_AXIS_ROUGHING", T("multi_axis_rough", Domains.MultiAxis) },
                { "MULTI_AXIS_WALL_FINISHING", T("multi_axis_wall_finish", Domains.MultiAxis) },
                { "MULTI_AXIS_DEBURRING", T("multi_axis_deburr", Domains.MultiAxis) },
                // 其他
                { "WEDM_OPERATION", T("wedm", Domains.Wedm) },
                { "PLANAR_ADDITIVE_DEPOSIT", T("additive_planar", Domains.Additive) },
                { "ROTARY_ADDITIVE_DEPOSIT", T("additive_rotary", Domains.Additive) },
                { "ON_MACHINE_PROBING", T("probe", Domains.Probing) },
                { "MILL_TOOL_PROBING", T("probe", Domains.Probing) },
                { "MILL_MACHINE_CONTROL", T("machine_control", Domains.MachineControl) },
                { "MILL_USER_DEFINED", T("user_defined", Domains.UserDefined) },
            };

        /// <summary>
        /// 恒返回 true：未知 typeName 也落 "other"（合法兜底值，§4.4），
        /// 返回值语义 = "存在映射结果"。
        /// </summary>
        public static bool TryMap(string typeName, out string operationType, out string domain)
        {
            if (Table.TryGetValue(typeName, out var mapped))
            {
                operationType = mapped.Item1;
                domain = mapped.Item2;
                return true;
            }
            operationType = "other";
            domain = Domains.Unknown;
            return true;
        }

        private static Tuple<string, string> T(string operationType, string domain)
        {
            return Tuple.Create(operationType, domain);
        }
    }
}
