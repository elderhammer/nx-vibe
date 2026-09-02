using System.Collections.Generic;

namespace Autocam.Plan.Core.Policies
{
    public enum ParamCategory
    {
        Strategy,
        Technology,
    }

    public enum ParamSource
    {
        /// <summary>默认继承链：方法组 → 几何组 → 刀具组 → 模板根（plan-exporter.md §4.3）。</summary>
        Default,
        Tool,
        Geometry,
    }

    public sealed class ParamSpec
    {
        public string Name { get; set; }
        public ParamCategory Category { get; set; }

        /// <summary>该参数的继承优先源（如 cross_over_distance 与刀具相关、tool_axis 与几何组相关）。</summary>
        public ParamSource Source { get; set; } = ParamSource.Default;
    }

    /// <summary>
    /// MVP 必填清单的参数表（数据与遍历逻辑隔离，扩表不改码）。
    /// 字段名 = plan/schema 字段名，来源：nx-plugin-design.md §5 + nxopen-research.md §4.3/§4.4。
    /// 非切削细分/避让点/多轴驱动按"可选增强"暂不入表（nxopen-research.md §4.9 注）。
    /// </summary>
    public static class ParamRegistry
    {
        public static readonly List<ParamSpec> All = new List<ParamSpec>
        {
            // ---- strategy（nxopen-research.md §4.3）----
            new ParamSpec { Name = "cut_pattern" },
            new ParamSpec { Name = "cut_order" },
            new ParamSpec { Name = "cut_direction" },
            new ParamSpec { Name = "depth_per_cut" },
            new ParamSpec { Name = "stepover" },
            new ParamSpec { Name = "finish_passes" },
            new ParamSpec { Name = "multi_depth_cut" },
            new ParamSpec { Name = "part_stock" },
            new ParamSpec { Name = "floor_stock" },
            new ParamSpec { Name = "wall_stock" },
            new ParamSpec { Name = "wall_cleanup" },
            new ParamSpec { Name = "tool_axis", Source = ParamSource.Geometry },
            new ParamSpec { Name = "cycle" },
            new ParamSpec { Name = "depth" },
            new ParamSpec { Name = "bottom_stock" },
            new ParamSpec { Name = "bottom_clearance" },
            new ParamSpec { Name = "top_offset" },
            new ParamSpec { Name = "control_point_offset" },
            new ParamSpec { Name = "retract_output_mode" },
            new ParamSpec { Name = "tool_drive_point" },
            new ParamSpec { Name = "cross_over_distance", Source = ParamSource.Tool },
            new ParamSpec { Name = "non_cutting" },

            // ---- technology（nxopen-research.md §4.4）----
            new ParamSpec { Name = "spindle_mode", Category = ParamCategory.Technology },
            new ParamSpec { Name = "spindle_rpm", Category = ParamCategory.Technology },
            new ParamSpec { Name = "surface_speed", Category = ParamCategory.Technology },
            new ParamSpec { Name = "feed_cut", Category = ParamCategory.Technology },
            new ParamSpec { Name = "feed_approach", Category = ParamCategory.Technology },
            new ParamSpec { Name = "feed_engage", Category = ParamCategory.Technology },
            new ParamSpec { Name = "feed_departure", Category = ParamCategory.Technology },
            new ParamSpec { Name = "retract_speed", Category = ParamCategory.Technology },
            new ParamSpec { Name = "coolant", Category = ParamCategory.Technology },
            new ParamSpec { Name = "tolerance", Category = ParamCategory.Technology },
            new ParamSpec { Name = "minimal_clearance", Category = ParamCategory.Technology },
        };

        /// <summary>tools[] 的 MVP 字段（nx-plugin-design.md §5；type/diameter/num_flutes 必填，(flute_length)/lower_corner_radius 建议）。</summary>
        public static readonly string[] ToolFields =
        {
            "type", "diameter", "num_flutes", "flute_length", "lower_corner_radius",
        };

        /// <summary>setups[].mcs / 安全平面 / 夹具偏置的组参数名（Geometry 组）。</summary>
        public static readonly string[] SetupFields =
        {
            "origin", "z_axis", "x_axis", "safe_plane_z", "fixture_offset",
        };
    }
}
