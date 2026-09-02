using System;
using System.Collections.Generic;

namespace Autocam.PlanComparer.Core.Compare.Tolerance
{
    public enum ToleranceKind
    {
        /// <summary>严格相等（枚举/整数/布尔/未入表字段的保守默认）。</summary>
        Exact,

        /// <summary>绝对容差，单位 mm：|Δ| ≤ 0.01。</summary>
        AbsoluteMm,

        /// <summary>相对容差：|Δ|/max(|L|,|R|) ≤ 5%（转速/进给口径）。</summary>
        RelativePercent,

        /// <summary>向量欧氏距离 ≤ 0.01 mm（mcs 轴/锚点）。</summary>
        VectorMm,
    }

    public sealed class ToleranceSpec
    {
        public ToleranceKind Kind { get; set; }
        public double Value { get; set; }
    }

    /// <summary>
    /// 容差表（决策点 D4，plan-comparer.md §3.4）——数据与遍历逻辑隔离，扩表不改码。
    /// 键 = plan 字段路径（复合对象用点分路径，如 "stepover.value"）。
    /// 未入表的数值字段：调用侧按 Exact 判定并告警（保守默认，绝不静默放行未定义口径）；
    /// 字符串/布尔字段一律相等判定，不查表。
    /// </summary>
    public static class ToleranceRegistry
    {
        public const double Mm = 0.01;
        public const double Relative = 5.0;   // %

        private static readonly Dictionary<string, ToleranceSpec> Table =
            new Dictionary<string, ToleranceSpec>(StringComparer.Ordinal)
            {
                // ---- 线性尺寸：绝对 0.01mm（strategy/technology/刀具/MCS）----
                { "depth_per_cut", S(ToleranceKind.AbsoluteMm, Mm) },
                { "part_stock", S(ToleranceKind.AbsoluteMm, Mm) },
                { "floor_stock", S(ToleranceKind.AbsoluteMm, Mm) },
                { "wall_stock", S(ToleranceKind.AbsoluteMm, Mm) },
                { "depth.value", S(ToleranceKind.AbsoluteMm, Mm) },
                { "bottom_stock", S(ToleranceKind.AbsoluteMm, Mm) },
                { "bottom_clearance", S(ToleranceKind.AbsoluteMm, Mm) },
                { "top_offset", S(ToleranceKind.AbsoluteMm, Mm) },
                { "control_point_offset", S(ToleranceKind.AbsoluteMm, Mm) },
                { "cross_over_distance", S(ToleranceKind.AbsoluteMm, Mm) },
                { "minimal_clearance", S(ToleranceKind.AbsoluteMm, Mm) },
                { "tolerance", S(ToleranceKind.AbsoluteMm, Mm) },
                { "diameter", S(ToleranceKind.AbsoluteMm, Mm) },
                { "flute_length", S(ToleranceKind.AbsoluteMm, Mm) },
                { "lower_corner_radius", S(ToleranceKind.AbsoluteMm, Mm) },
                { "safe_plane_z", S(ToleranceKind.AbsoluteMm, Mm) },

                // ---- 转速/进给：相对 5% ----
                { "spindle_rpm", S(ToleranceKind.RelativePercent, Relative) },
                { "surface_speed", S(ToleranceKind.RelativePercent, Relative) },
                { "feed_cut.value", S(ToleranceKind.RelativePercent, Relative) },
                { "feed_approach.value", S(ToleranceKind.RelativePercent, Relative) },
                { "feed_engage.value", S(ToleranceKind.RelativePercent, Relative) },
                { "feed_departure.value", S(ToleranceKind.RelativePercent, Relative) },
                { "retract_speed", S(ToleranceKind.RelativePercent, Relative) },

                // ---- 向量：欧氏距离 0.01mm ----
                { "origin", S(ToleranceKind.VectorMm, Mm) },
                { "z_axis", S(ToleranceKind.VectorMm, Mm) },
                { "x_axis", S(ToleranceKind.VectorMm, Mm) },
                { "anchor_point", S(ToleranceKind.VectorMm, Mm) },

                // ---- 数值但语义精确（百分比/编号，显式入表避免误告警）----
                { "stepover.value", S(ToleranceKind.Exact, 0) },
                { "num_flutes", S(ToleranceKind.Exact, 0) },
                { "fixture_offset", S(ToleranceKind.Exact, 0) },
                { "finish_passes", S(ToleranceKind.Exact, 0) },
            };

        public static bool TryLookup(string fieldPath, out ToleranceSpec spec)
        {
            return Table.TryGetValue(fieldPath, out spec);
        }

        public static ToleranceSpec Default => S(ToleranceKind.Exact, 0);

        private static ToleranceSpec S(ToleranceKind kind, double value)
        {
            return new ToleranceSpec { Kind = kind, Value = value };
        }
    }
}
