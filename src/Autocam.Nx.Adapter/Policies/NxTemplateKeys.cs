using System;
using System.Collections.Generic;

namespace Autocam.Nx.Adapter.Policies
{
    /// <summary>
    /// NX 模板注册键表（M2_Probe2 实测，GUI 会话 GetTemplateSubtypes 枚举）：
    /// Create* 键语义 = (setup 族, 视图类 subtype)，不是旧式 typeName——
    /// 例如 CreateMethod(parent, "mill_planar", "MILL_METHOD", …)，族必须已注册（17 setup 族之一）。
    /// 工序键：plan 导出类型（TypeNameResolver：Builder 类名映射）→ 注册 subtype（反向表由探针 ③ 实测），
    /// 保证重建工序的 Builder 能映射回同一 plan 类型（round-trip 同构）。
    /// 多 subtype 同 Builder（如 FACE_MILLING ← Facing/FacingZigZag/FaceMilling）无法反推原始 subtype——
    /// 表中值取常见默认；template 默认参数差会在 plan″ 以偏差行显形，不静默。
    /// 未入表 plan 类型：CreateOperation 前诊断 ERROR + 跳过（执行忠实，绝不创建错类型）。
    /// </summary>
    public static class NxTemplateKeys
    {
        /// <summary>重建载体 setup 族（与 SessionBootstrap.CamSetupTemplate 同源）。</summary>
        public const string SetupFamily = "mill_planar";

        // 四视图组 subtype（探针 v1 ② 各视图类枚举，均在 mill_planar 族下注册）
        public const string ProgramGroup = "PROGRAM";
        public const string MethodGroup = "MILL_METHOD";
        public const string ToolGroupMill = "MILL";
        public const string ToolGroupDrill = "DRILL";   // 待 hole_making 族 Tool 枚举验证（当前 part 无钻头）
        public const string GeometryGroupMcs = "MCS";

        /// <summary>plan 工序类型（导出 TypeNameResolver 口径）→ mill_planar 族 Operation subtype。</summary>
        public static readonly Dictionary<string, string> OperationSubtypeByPlanType =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // 探针 ③ 实测：FACE_MILL_* → Facing*Builder → 导出 FACE_MILLING
                { "FACE_MILLING", "FACE_MILL_ZIGZAG" },
                // PLANAR_MILL/PLANAR_PROFILING 同为 PlanarMillingBuilder，取同名
                { "PLANAR_MILL", "PLANAR_MILL" },
                { "GROOVE_MILL", "GROOVE_MILLING" },
                { "ENGRAVE", "PLANAR_TEXT" },
                { "CHAMFER_MILL", "PLANAR_DEBURRING" },
                { "MILL_MACHINE_CONTROL", "MILL_CONTROL" },
                { "DOCUMENTATION", "DOCUMENTATION" },
                // VolumeBased25D 系（FLOOR_FACING/FLOOR_WALL/POCKETING/WALL_PROFILING/WALL_FLOOR_PROFILING
                // 同 Builder，原始 subtype 不可反推；POCKETING 为通用默认）
                { "UNKNOWN_VolumeBased25DMillingOperationBuilder", "POCKETING" },
            };

        public static string ResolveOperationSubtype(string planType)
        {
            if (planType == null)
            {
                return null;
            }
            return OperationSubtypeByPlanType.TryGetValue(planType, out var subtype) ? subtype : null;
        }
    }
}
