using System;
using System.Collections.Generic;
using System.Linq;
using Autocam.Plan.Core.Diagnostics;
using Autocam.Plan.Core.Dto;
using Autocam.PlanComparer.Core.Compare.Tolerance;
using Autocam.PlanComparer.Core.Report;

namespace Autocam.PlanComparer.Core.Compare.Dimensions
{
    /// <summary>
    /// strategy/technology 字典比较引擎（plan-comparer.md §4.5，parameter 与 strategy
    /// 两维度共用）：键并集按 Ordinal 序——right 缺（left 有 right 无）→ missing
    /// （顶层且 ∈ RightCapability.UnsupportedParams → known_skip，决策点 D6）、
    /// right 多出（left 无 right 有）→ extra、共有 → 复合对象递归 / 叶子值 ValueComparer。
    /// </summary>
    public static class ParamDictComparer
    {
        public static void CompareDicts(
            SortedDictionary<string, object> leftDict,
            SortedDictionary<string, object> rightDict,
            string dimension,
            string operationRef,
            CapabilityProfile rightCapability,
            List<DeviationEntry> rows,
            DiagnosticsCollector diag,
            CompareTally tally)
        {
            CompareDictsRec(leftDict, rightDict, null, dimension, operationRef, rightCapability, true, rows, diag, tally);
        }

        private static void CompareDictsRec(
            SortedDictionary<string, object> leftDict,
            SortedDictionary<string, object> rightDict,
            string pathPrefix,
            string dimension,
            string operationRef,
            CapabilityProfile rightCapability,
            bool topLevel,
            List<DeviationEntry> rows,
            DiagnosticsCollector diag,
            CompareTally tally)
        {
            var keys = new SortedSet<string>(leftDict.Keys.Union(rightDict.Keys), StringComparer.Ordinal);
            foreach (var key in keys)
            {
                var path = pathPrefix == null ? key : pathPrefix + "." + key;
                if (!leftDict.TryGetValue(key, out var lv))
                {
                    // right 多出（left 无 right 有）→ extra（无跳过豁免——豁免只对 right 缺的顶层字段）
                    rows.Add(Row(dimension, path, DeviationKinds.Extra, operationRef, null, rightDict[key], "right 侧多出该字段（plan-comparer.md §4.5）"));
                    continue;
                }
                if (!rightDict.TryGetValue(key, out var rv))
                {
                    // right 缺（left 有 right 无）→ missing；顶层且能力跳过 → known_skip（§3.8，D6）
                    if (topLevel && rightCapability != null && rightCapability.UnsupportedParams.Contains(key))
                    {
                        rows.Add(new DeviationEntry
                        {
                            Dimension = dimension,
                            OperationRef = operationRef,
                            Field = path,
                            Kind = DeviationKinds.KnownSkip,
                            Severity = DiagnosticsCollector.LevelInfo,
                            Left = lv,
                            Detail = string.Format("right 侧缺字段 {0}：重建侧能力跳过（plan-comparer.md §3.8）", path),
                        });
                    }
                    else
                    {
                        rows.Add(Row(dimension, path, DeviationKinds.Missing, operationRef, lv, null, "right 侧缺该字段（plan-comparer.md §4.5）"));
                    }
                    continue;
                }

                var normalizedLeft = ValueComparer.Normalize(lv);
                var normalizedRight = ValueComparer.Normalize(rv);
                var leftSub = AsDict(normalizedLeft);
                var rightSub = AsDict(normalizedRight);
                if (leftSub != null && rightSub != null)
                {
                    // 复合对象：递归到叶子路径（§4.4）
                    CompareDictsRec(leftSub, rightSub, path, dimension, operationRef, rightCapability, false, rows, diag, tally);
                    continue;
                }

                var t = DimensionTally(dimension, tally);
                t.Compared++;
                var outcome = ValueComparer.Compare(lv, rv, path, dimension, operationRef, rows, diag, tally);
                if (outcome.IsMatch)
                {
                    t.Matched++;
                }
            }
        }

        private static SortedDictionary<string, object> AsDict(object value)
        {
            var dict = value as IDictionary<string, object>;
            if (dict == null)
            {
                return null;
            }
            return new SortedDictionary<string, object>(dict, StringComparer.Ordinal);
        }

        private static DimensionTally DimensionTally(string dimension, CompareTally tally)
        {
            if (dimension == ReportDimensions.Parameter)
            {
                return tally.Parameter;
            }
            return tally.Strategy;
        }

        private static DeviationEntry Row(string dimension, string field, string kind, string operationRef, object left, object right, string detail)
        {
            return new DeviationEntry
            {
                Dimension = dimension,
                OperationRef = operationRef,
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
