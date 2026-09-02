using System;
using System.Collections.Generic;

namespace Autocam.Nx.Adapter.Policies
{
    /// <summary>
    /// plan 字段名 → Builder 属性路径表（隔离轴 4 适配层版本，扩表不改码）。
    /// 路径以 OperationBuilder（CreateBuilder 返回的运行时子类）为根，
    /// 覆盖 MVP 字段集（plan-exporter.md §2.1 / nxopen-research §4.3-4.4 实测口径）。
    /// 未入表字段：读取失败 → warning + 缺项（绝不伪造值）。
    /// </summary>
    public static class NxParamPaths
    {
        /// <summary>plan strategy/technology 字段 → 属性路径。</summary>
        public static readonly Dictionary<string, string> Operation =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "depth_per_cut", "DepthPerCut" },
                { "floor_stock", "CutParameters.FloorStock" },
                { "wall_stock", "CutParameters.WallStock" },
                { "part_stock", "CutParameters.PartStock" },
                { "stepover", "CutParameters.Stepover" },
                { "cut_order", "CutParameters.CutOrder" },
                { "cut_direction", "CutParameters.CutDirection" },
                { "finish_passes", "CutParameters.FinishPasses" },
                { "multi_depth_cut", "CutParameters.MultiDepthCut" },
                { "boundary_in_tol", "CutParameters.BoundaryInTol" },
                { "boundary_out_tol", "CutParameters.BoundaryOutTol" },
                { "spindle_rpm", "FeedsBuilder.SpindleRpmBuilder" },
                { "surface_speed", "FeedsBuilder.SurfaceSpeedBuilder" },
                { "feed_cut", "FeedsBuilder.FeedCutBuilder" },
                { "feed_approach", "FeedsBuilder.FeedApproachBuilder" },
                { "feed_engage", "FeedsBuilder.FeedEngageBuilder" },
                { "feed_departure", "FeedsBuilder.FeedDepartureBuilder" },
                { "retract_speed", "FeedsBuilder.RetractSpeed" },
                { "bottom_stock", "CuttingParameters.BottomStock" },
                { "bottom_clearance", "CuttingParameters.BottomClearance" },
                { "minimal_clearance", "CuttingParameters.MinimalClearance" },
                { "top_offset", "CuttingParameters.TopOffset" },
                { "control_point_offset", "ControlPointOffset" },
                { "retract_output_mode", "RetractOutputMode" },
            };

        /// <summary>刀具组字段 → 刀具 Builder 属性路径。</summary>
        public static readonly Dictionary<string, string> Tool =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "diameter", "TlDiameterBuilder" },
                { "num_flutes", "TlNumFlutesBuilder" },
                { "flute_length", "TlFluteLnBuilder" },
                { "lower_corner_radius", "TlLowCorRadBuilder" },
            };
    }
}
