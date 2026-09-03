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
            string planType,
            CapabilityProfile rightCapability,
            List<DeviationEntry> rows,
            DiagnosticsCollector diag,
            CompareTally tally)
        {
            CompareDictsRec(leftDict, rightDict, null, dimension, operationRef, planType, rightCapability, true, rows, diag, tally);
        }

        private static void CompareDictsRec(
            SortedDictionary<string, object> leftDict,
            SortedDictionary<string, object> rightDict,
            string pathPrefix,
            string dimension,
            string operationRef,
            string planType,
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
                    // right 缺（left 有 right 无）→ missing；顶层且命中豁免表 → known_skip（§3.8，D6）
                    var skipSource = TrySkipSource(key, planType, rightCapability);
                    if (topLevel && skipSource != null)
                    {
                        rows.Add(new DeviationEntry
                        {
                            Dimension = dimension,
                            OperationRef = operationRef,
                            Field = path,
                            Kind = DeviationKinds.KnownSkip,
                            Severity = DiagnosticsCollector.LevelInfo,
                            Left = lv,
                            Detail = string.Format("right 侧缺字段 {0}：{1}（plan-comparer.md §3.8）", path, skipSource),
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
                    CompareDictsRec(leftSub, rightSub, path, dimension, operationRef, planType, rightCapability, false, rows, diag, tally);
                    continue;
                }

                // NX 写保护（顶层、两侧都有值且值不等）：重建侧模板默认被显式化（E1 实测
                // 新建 op 模板默认 InheritanceStatus=False），与原件显式值比较必偏差——
                // 命中写保护表 → known_skip + 跳过值比较（绝不静默：判定必须命中结构化表）。
                // 值相等 → 走正常比较（§3.1 自反性：Compare(P,P) 不产生豁免行）。
                var protectSource = TrySkipSource(key, planType, rightCapability);
                if (topLevel && protectSource != null && !ValueComparer.AreEquivalent(lv, rv, path))
                {
                    rows.Add(new DeviationEntry
                    {
                        Dimension = dimension,
                        OperationRef = operationRef,
                        Field = path,
                        Kind = DeviationKinds.KnownSkip,
                        Severity = DiagnosticsCollector.LevelInfo,
                        Left = lv,
                        Right = rv,
                        Detail = string.Format("字段 {0}：{1}——两侧值差异为 NX 模板固化，plan 无法驱动（plan-comparer.md §3.8）", path, protectSource),
                    });
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

        /// <summary>
        /// 豁免判定（结构化查表，绝不解析文本）：UnsupportedParams（版本缺能力，按参数名全局）
        /// 或 UnwritableByPlanType（NX 写保护，按工序类型×字段细粒度）。命中返回豁免来源描述，未命中 null。
        /// </summary>
        private static string TrySkipSource(string key, string planType, CapabilityProfile rightCapability)
        {
            if (rightCapability == null)
            {
                return null;
            }
            if (rightCapability.UnsupportedParams.Contains(key))
            {
                return "重建侧能力跳过";
            }
            if (planType != null
                && rightCapability.UnwritableByPlanType.TryGetValue(planType, out var fields)
                && fields.Contains(key))
            {
                return string.Format("NX 写保护（类型 {0}）", planType);
            }
            return null;
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
