using System.Collections.Generic;

namespace Autocam.PlanComparer.Core.Compare
{
    /// <summary>
    /// 比较过程计数（汇总/评分的数据源）：各维度比较器把"一致"也计入——
    /// 偏差行只记不一致，一致数只在 tally 里（plan-comparer.md §4.6）。
    /// 评分单调性（I2）靠它：修复偏差 → tally 一致数上升、均值下降。
    /// </summary>
    public sealed class CompareTally
    {
        // ---- structure（来自对齐器）----
        public int MatchedOps { get; set; }
        public int TotalOps { get; set; }

        // ---- 各维度（比较器自增：Compared/Match 数）----
        public DimensionTally Tool { get; } = new DimensionTally();
        public DimensionTally Parameter { get; } = new DimensionTally();
        public DimensionTally Strategy { get; } = new DimensionTally();
        public DimensionTally Mcs { get; } = new DimensionTally();

        // ---- geometry ----
        public int GeometryCompared { get; set; }
        public int GeometryMatched { get; set; }
        public int GeometryTotal { get; set; }
        public int GeometryCollisions { get; set; }

        /// <summary>
        /// 全部配对数值字段的相对偏差 r = |Δ|/max(|L|,|R|)（一致字段 r=0 也计入），
        /// param_deviation_mean = 均值（§4.6）。覆盖 strategy/technology/刀具数值字段。
        /// </summary>
        public List<double> ParamRelativeDeviations { get; } = new List<double>();
    }

    public sealed class DimensionTally
    {
        public int Compared { get; set; }
        public int Matched { get; set; }
    }
}
