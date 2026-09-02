using System.Collections.Generic;
using System.Linq;
using Autocam.PlanComparer.Core.Report;

namespace Autocam.PlanComparer.Core.Compare.Scoring
{
    /// <summary>
    /// 评分聚合（plan-comparer.md §4.6）：
    /// summary 计数 = 偏差行按 (dimension, kind) 归类（missing/extra/deviations/
    /// known_skips/type_mismatches/order_swaps/group_diffs）+ tally 的一致数；
    /// scores = §2.3 公式（分母取 max，对称性 §3.2；空对空 → 1.0/0.0/1.0）。
    /// </summary>
    public static class ScoringAggregator
    {
        public static void Build(List<DeviationEntry> rows, CompareTally tally, SummaryEntry summary, ScoresEntry scores)
        {
            // ---- structure（工序级计数 = 行带 operation_ref；组级行并入 group_diffs）----
            summary.Structure.MatchedOps = tally.MatchedOps;
            summary.Structure.TotalOps = tally.TotalOps;
            summary.Structure.Missing = Count(rows, ReportDimensions.Structure, DeviationKinds.Missing, true);
            summary.Structure.Extra = Count(rows, ReportDimensions.Structure, DeviationKinds.Extra, true);
            summary.Structure.TypeMismatches = Count(rows, ReportDimensions.Structure, DeviationKinds.TypeMismatch, true);
            summary.Structure.OrderSwaps = Count(rows, ReportDimensions.Structure, DeviationKinds.OrderSwap, true);
            summary.Structure.GroupDiffs = rows.Count(r => r.Dimension == ReportDimensions.Structure
                && string.IsNullOrEmpty(r.OperationRef) && r.Kind != DeviationKinds.Unaligned);

            // ---- 各维度（tally 供一致数，行供不一致数）----
            Fill(summary.Tool, tally.Tool, rows, ReportDimensions.Tool);
            Fill(summary.Parameter, tally.Parameter, rows, ReportDimensions.Parameter);
            summary.Parameter.KnownSkips = Count(rows, ReportDimensions.Parameter, DeviationKinds.KnownSkip, false);
            Fill(summary.Strategy, tally.Strategy, rows, ReportDimensions.Strategy);
            summary.Strategy.KnownSkips = Count(rows, ReportDimensions.Strategy, DeviationKinds.KnownSkip, false);
            Fill(summary.Mcs, tally.Mcs, rows, ReportDimensions.Mcs);

            summary.Geometry.Compared = tally.GeometryCompared;
            summary.Geometry.Matched = tally.GeometryMatched;
            summary.Geometry.Missing = Count(rows, ReportDimensions.Geometry, DeviationKinds.Missing, false);
            summary.Geometry.Extra = Count(rows, ReportDimensions.Geometry, DeviationKinds.Extra, false);
            summary.Geometry.Deviations = Count(rows, ReportDimensions.Geometry, DeviationKinds.Deviation, false);
            summary.Geometry.Collisions = tally.GeometryCollisions;

            // ---- scores（§2.3 公式）----
            scores.StructureConsistency = tally.TotalOps == 0 ? 1.0 : (double)tally.MatchedOps / tally.TotalOps;
            scores.ParamDeviationMean = tally.ParamRelativeDeviations.Count == 0
                ? 0.0
                : tally.ParamRelativeDeviations.Average();
            scores.GeometryMatchRate = tally.GeometryTotal == 0 ? 1.0 : (double)tally.GeometryMatched / tally.GeometryTotal;
        }

        private static void Fill(DimensionSummaryEntry summary, DimensionTally tally, List<DeviationEntry> rows, string dimension)
        {
            summary.Compared = tally.Compared;
            summary.Matched = tally.Matched;
            summary.Missing = Count(rows, dimension, DeviationKinds.Missing, false);
            summary.Extra = Count(rows, dimension, DeviationKinds.Extra, false);
            summary.Deviations = Count(rows, dimension, DeviationKinds.Deviation, false);
        }

        private static int Count(List<DeviationEntry> rows, string dimension, string kind, bool requireOperationRef)
        {
            return rows.Count(r => r.Dimension == dimension && r.Kind == kind
                && (!requireOperationRef || !string.IsNullOrEmpty(r.OperationRef)));
        }
    }
}
