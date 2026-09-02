using System.Collections.Generic;
using Autocam.Plan.Core.Diagnostics;
using Autocam.Plan.Core.Plan;
using Autocam.Plan.Core.Policies;
using Autocam.PlanComparer.Core.Compare.Alignment;
using Autocam.PlanComparer.Core.Compare.Tolerance;
using Autocam.PlanComparer.Core.Report;

namespace Autocam.PlanComparer.Core.Compare.Dimensions
{
    /// <summary>
    /// 刀具维度（plan-comparer.md §4.5）：配对工序经 tool_ref 解析的刀具逐字段
    /// （ParamRegistry.ToolFields 序）比较；tool_ref 悬空 → unaligned 行 + error；
    /// 两侧刀具表计数不同 → missing/extra 行（field="tools"，未引用刀具的增删也可见）。
    /// </summary>
    public static class ToolComparer
    {
        public static void Compare(
            List<OpPair> pairs,
            SideModel left,
            SideModel right,
            List<DeviationEntry> rows,
            DiagnosticsCollector diag,
            CompareTally tally)
        {
            foreach (var pair in pairs)
            {
                var opRef = pair.Left.Op.OperationId;
                if (!left.ToolById.TryGetValue(pair.Left.Op.ToolRef, out var leftTool))
                {
                    Unaligned(opRef, pair.Left.Op.ToolRef, "left", rows, diag);
                    continue;
                }
                if (!right.ToolById.TryGetValue(pair.Right.Op.ToolRef, out var rightTool))
                {
                    Unaligned(opRef, pair.Right.Op.ToolRef, "right", rows, diag);
                    continue;
                }

                foreach (var field in ParamRegistry.ToolFields)
                {
                    var lv = ToolField(leftTool, field);
                    var rv = ToolField(rightTool, field);
                    if (lv == null && rv == null)
                    {
                        continue;
                    }
                    if (lv == null)
                    {
                        rows.Add(Row(field, DeviationKinds.Extra, opRef, null, rv, "right 侧多出该刀具字段（plan-comparer.md §4.5）"));
                        continue;
                    }
                    if (rv == null)
                    {
                        rows.Add(Row(field, DeviationKinds.Missing, opRef, lv, null, "right 侧缺该刀具字段（plan-comparer.md §4.5）"));
                        continue;
                    }
                    tally.Tool.Compared++;
                    var outcome = ValueComparer.Compare(lv, rv, field, ReportDimensions.Tool, opRef, rows, diag, tally);
                    if (outcome.IsMatch)
                    {
                        tally.Tool.Matched++;
                    }
                }
            }

            if (left.ToolById.Count > right.ToolById.Count)
            {
                rows.Add(new DeviationEntry
                {
                    Dimension = ReportDimensions.Tool,
                    Kind = DeviationKinds.Missing,
                    Severity = DiagnosticsCollector.LevelWarning,
                    Field = "tools",
                    Detail = string.Format("right 侧刀具数 {0} 少于 left {1}（plan-comparer.md §4.5）", right.ToolById.Count, left.ToolById.Count),
                });
            }
            else if (left.ToolById.Count < right.ToolById.Count)
            {
                rows.Add(new DeviationEntry
                {
                    Dimension = ReportDimensions.Tool,
                    Kind = DeviationKinds.Extra,
                    Severity = DiagnosticsCollector.LevelWarning,
                    Field = "tools",
                    Detail = string.Format("right 侧刀具数 {0} 多于 left {1}（plan-comparer.md §4.5）", right.ToolById.Count, left.ToolById.Count),
                });
            }
        }

        private static object ToolField(ToolEntry tool, string field)
        {
            switch (field)
            {
                case "type": return tool.Type;
                case "diameter": return tool.Diameter;
                case "num_flutes": return tool.NumFlutes;
                case "flute_length": return tool.FluteLength;
                case "lower_corner_radius": return tool.LowerCornerRadius;
                default: return null;
            }
        }

        private static void Unaligned(string opRef, string toolRef, string sideName, List<DeviationEntry> rows, DiagnosticsCollector diag)
        {
            var detail = string.Format("工序 {0} 的 tool_ref={1} 在 {2} 侧悬空，跳过刀具对比（plan-comparer.md §4.5）", opRef, toolRef, sideName);
            rows.Add(new DeviationEntry
            {
                Dimension = ReportDimensions.Tool,
                OperationRef = opRef,
                Field = toolRef,
                Kind = DeviationKinds.Unaligned,
                Severity = DiagnosticsCollector.LevelError,
                Detail = detail,
            });
            diag.Error("REFERENCE_DANGLING", detail);
        }

        private static DeviationEntry Row(string field, string kind, string opRef, object left, object right, string detail)
        {
            return new DeviationEntry
            {
                Dimension = ReportDimensions.Tool,
                OperationRef = opRef,
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
