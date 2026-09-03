using System;
using System.Collections.Generic;

namespace Autocam.Nx.Adapter.Policies
{
    /// <summary>
    /// NX 写保护表：plan 工序类型 × 字段 → NXOpen 层无条件回滚/不可表达（M3_Probe E 段实测）：
    /// - FacingZigZagBuilder（NX2406 Facing 全系 incl. FACE_MILLING_MANUAL，E2 实测同名 builder）：
    ///   FloorStock/PartStock 写入即回滚（E1 一段式/两段式终态同回滚 0.2/1.0，InheritanceStatus 一并回滚）；
    ///   DepthPerCut 属性不存在（E2 路径 ✗）。
    /// - EdgeChamferBuilder：FloorStock/PartStock 同回滚（D 段实测）。
    /// 语义：这些字段的重建值由 NX 模板固化，plan 无法驱动——执行侧跳过写入（warning），
    /// 比较侧按同表做 known_skip 豁免（绝不静默：豁免判定必须命中本表）。
    /// 键 = plan operation_type 口径（TypeMapper 归一输出，如 mill_face/mill_chamfer——
    /// 与比较器 pair.Left.Op.OperationType 及 PlanExecutorPipeline 命令 PlanOperationType 同口径），
    /// 扩表不改码。
    /// </summary>
    public static class NxWriteProtection
    {
        public static readonly Dictionary<string, string[]> FieldsByPlanType =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                { "mill_face", new[] { "floor_stock", "part_stock", "depth_per_cut" } },
                { "mill_chamfer", new[] { "floor_stock", "part_stock" } },
            };

        public static bool IsProtected(string planType, string field)
        {
            return planType != null
                && FieldsByPlanType.TryGetValue(planType, out var fields)
                && Array.IndexOf(fields, field) >= 0;
        }
    }
}
