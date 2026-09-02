using System;
using System.Collections.Generic;
using System.Linq;
using Autocam.Plan.Core.Diagnostics;
using Autocam.PlanComparer.Core.Compare.Alignment;
using Autocam.PlanComparer.Core.Compare.Tolerance;
using Autocam.PlanComparer.Core.Report;

namespace Autocam.PlanComparer.Core.Compare.Dimensions
{
    /// <summary>
    /// 几何维度（plan-comparer.md §4.5，MVP = anchor_point 兜底，FaceResolver 到位后
    /// 升级为面集匹配）：配对工序的 feature.anchor_point 双侧有 → VectorMm 0.01；
    /// 单侧 → missing/extra。对称碰撞检测（镜像导出器 §4.5）：左锚点 0.01 内命中
    /// 多个右锚点 → warning 诊断（按左 features 列表序，每个左锚点至多一条）。
    /// </summary>
    public static class GeometryComparer
    {
        public static void Compare(
            List<OpPair> pairs,
            SideModel left,
            SideModel right,
            List<DeviationEntry> rows,
            DiagnosticsCollector diag,
            CompareTally tally)
        {
            tally.GeometryTotal = Math.Max(left.FeatureById.Count, right.FeatureById.Count);
            WarnAmbiguousAnchors(left, right, diag, tally);

            foreach (var pair in pairs)
            {
                var opRef = pair.Left.Op.OperationId;
                var la = pair.Left.Feature.GeometryRef?.AnchorPoint;
                var ra = pair.Right.Feature.GeometryRef?.AnchorPoint;
                if (la == null && ra == null)
                {
                    continue;
                }
                if (la == null)
                {
                    rows.Add(Row(DeviationKinds.Extra, opRef, null, ra, "right 侧多出锚点（plan-comparer.md §4.5）"));
                    continue;
                }
                if (ra == null)
                {
                    rows.Add(Row(DeviationKinds.Missing, opRef, la, null, "right 侧缺锚点（plan-comparer.md §4.5）"));
                    continue;
                }
                tally.GeometryCompared++;
                var outcome = ValueComparer.Compare(la, ra, "anchor_point", ReportDimensions.Geometry, opRef, rows, diag, tally);
                if (outcome.IsMatch)
                {
                    tally.GeometryMatched++;
                }
            }
        }

        private static void WarnAmbiguousAnchors(SideModel left, SideModel right, DiagnosticsCollector diag, CompareTally tally)
        {
            var rightAnchors = right.Plan.Features
                .Where(f => f.GeometryRef?.AnchorPoint != null)
                .Select(f => ToList(f.GeometryRef.AnchorPoint))
                .ToList();

            foreach (var lf in left.Plan.Features)
            {
                if (lf.GeometryRef?.AnchorPoint == null)
                {
                    continue;
                }
                var anchor = ToList(lf.GeometryRef.AnchorPoint);
                var hits = rightAnchors.Count(ra => ValueComparer.Distance(anchor, ra) <= ToleranceRegistry.Mm);
                if (hits > 1)
                {
                    tally.GeometryCollisions++;
                    diag.Warning("GEOMETRY_AMBIGUOUS",
                        string.Format("特征 {0} 的锚点在 0.01mm 内命中 {1} 个 right 侧锚点（对称/阵列面碰撞，需人工复核，plan-comparer.md §4.5）",
                            lf.FeatureId, hits));
                }
            }
        }

        private static List<double> ToList(double[] vector)
        {
            return new List<double>(vector);
        }

        private static DeviationEntry Row(string kind, string opRef, object left, object right, string detail)
        {
            return new DeviationEntry
            {
                Dimension = ReportDimensions.Geometry,
                OperationRef = opRef,
                Field = "anchor_point",
                Kind = kind,
                Severity = DiagnosticsCollector.LevelWarning,
                Left = left,
                Right = right,
                Detail = detail,
            };
        }
    }
}
