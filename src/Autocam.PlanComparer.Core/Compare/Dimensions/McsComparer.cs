using System;
using System.Collections.Generic;
using Autocam.Plan.Core.Diagnostics;
using Autocam.Plan.Core.Plan;
using Autocam.PlanComparer.Core.Compare.Alignment;
using Autocam.PlanComparer.Core.Compare.Tolerance;
using Autocam.PlanComparer.Core.Report;

namespace Autocam.PlanComparer.Core.Compare.Dimensions
{
    /// <summary>
    /// MCS/装夹维度（plan-comparer.md §4.5）：setups 按位置配对——
    /// origin/z_axis/x_axis（VectorMm）、safe_plane_z（AbsoluteMm）、fixture_offset（Exact）；
    /// 单侧缺 setup → missing/extra 行（field="setup"）、单侧缺 mcs → missing/extra 行（field="mcs"）。
    /// setup 级行 operation_ref 为空。
    /// </summary>
    public static class McsComparer
    {
        private static readonly string[] AxisFields = { "origin", "z_axis", "x_axis" };

        public static void Compare(
            SideModel left,
            SideModel right,
            List<DeviationEntry> rows,
            DiagnosticsCollector diag,
            CompareTally tally)
        {
            var lSetups = left.Plan.Setups;
            var rSetups = right.Plan.Setups;
            var min = Math.Min(lSetups.Count, rSetups.Count);

            for (var i = 0; i < min; i++)
            {
                var l = lSetups[i];
                var r = rSetups[i];
                if (l.Mcs == null && r.Mcs == null)
                {
                    continue;   // 双方都无 mcs：无可比内容
                }
                if (l.Mcs == null)
                {
                    rows.Add(Row("mcs", DeviationKinds.Extra, null, null, "right 侧多出 mcs（plan-comparer.md §4.5）"));
                    continue;
                }
                if (r.Mcs == null)
                {
                    rows.Add(Row("mcs", DeviationKinds.Missing, null, null, "right 侧缺 mcs（plan-comparer.md §4.5）"));
                    continue;
                }

                foreach (var field in AxisFields)
                {
                    CompareField(GetAxis(l.Mcs, field), GetAxis(r.Mcs, field), field, rows, diag, tally);
                }
                CompareField(l.SafePlaneZ, r.SafePlaneZ, "safe_plane_z", rows, diag, tally);
                CompareField(l.FixtureOffset, r.FixtureOffset, "fixture_offset", rows, diag, tally);
            }

            for (var i = min; i < lSetups.Count; i++)
            {
                rows.Add(Row("setup", DeviationKinds.Missing, lSetups[i].SetupId, null, string.Format("right 侧缺 setup {0}（plan-comparer.md §4.5）", lSetups[i].SetupId)));
            }
            for (var i = min; i < rSetups.Count; i++)
            {
                rows.Add(Row("setup", DeviationKinds.Extra, null, rSetups[i].SetupId, string.Format("right 侧多出 setup {0}（plan-comparer.md §4.5）", rSetups[i].SetupId)));
            }
        }

        private static double[] GetAxis(McsEntry mcs, string field)
        {
            switch (field)
            {
                case "origin": return mcs.Origin;
                case "z_axis": return mcs.ZAxis;
                default: return mcs.XAxis;
            }
        }

        private static void CompareField(object lv, object rv, string field, List<DeviationEntry> rows, DiagnosticsCollector diag, CompareTally tally)
        {
            if (lv == null && rv == null)
            {
                return;
            }
            if (lv == null)
            {
                rows.Add(Row(field, DeviationKinds.Extra, null, rv, "right 侧多出该字段（plan-comparer.md §4.5）"));
                return;
            }
            if (rv == null)
            {
                rows.Add(Row(field, DeviationKinds.Missing, lv, null, "right 侧缺该字段（plan-comparer.md §4.5）"));
                return;
            }
            tally.Mcs.Compared++;
            var outcome = ValueComparer.Compare(lv, rv, field, ReportDimensions.Mcs, null, rows, diag, tally);
            if (outcome.IsMatch)
            {
                tally.Mcs.Matched++;
            }
        }

        private static DeviationEntry Row(string field, string kind, object left, object right, string detail)
        {
            return new DeviationEntry
            {
                Dimension = ReportDimensions.Mcs,
                Field = field,
                Kind = kind,
                Severity = DiagnosticsCollector.LevelWarning,
                Left = left,
                Right = right,
                Detail = detail,
            };
        }
    }
}
