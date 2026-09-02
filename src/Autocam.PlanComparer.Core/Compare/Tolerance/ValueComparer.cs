using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autocam.Plan.Core.Diagnostics;
using Autocam.PlanComparer.Core.Report;
using Newtonsoft.Json.Linq;

namespace Autocam.PlanComparer.Core.Compare.Tolerance
{
    /// <summary>叶子值比较结论：IsMatch = 一致（含容差内）；Delta = right - left（相对口径为相对差，向量为距离）。</summary>
    public sealed class CompareOutcome
    {
        public bool IsMatch { get; set; }

        /// <summary>数值字段的有符号差（plan-comparer.md §3.2 对称性）。</summary>
        public double? Delta { get; set; }

        /// <summary>判定位容差值（口径见 §3.4 表）。</summary>
        public double? Tolerance { get; set; }

        /// <summary>类型不一致等说明（非一致时非空）。</summary>
        public string Detail { get; set; }
    }

    /// <summary>
    /// 叶子值比较器（plan-comparer.md §4.4）：数值按 ToleranceRegistry 口径、枚举串/布尔
    /// 相等判定、向量按欧氏距离。比较不一致时把偏差行写入 rows（调用侧管 missing/extra
    /// 与复合对象递归，本类只见叶子值）。值先经 JToken → CLR 归一（反序列化 plan 的
    /// strategy/technology 值是 JValue/JObject）。dimension ∈ {parameter, strategy, tool}
    /// 且口径为 AbsoluteMm/RelativePercent 的数值字段，其相对偏差计入 param_deviation_mean。
    /// </summary>
    public static class ValueComparer
    {
        public static CompareOutcome Compare(
            object left,
            object right,
            string fieldPath,
            string dimension,
            string operationRef,
            List<DeviationEntry> rows,
            DiagnosticsCollector diag,
            CompareTally tally)
        {
            left = Normalize(left);
            right = Normalize(right);

            var outcome = new CompareOutcome();
            if (IsNumber(left) && IsNumber(right))
            {
                return CompareNumbers(ToDouble(left), ToDouble(right), fieldPath, dimension, operationRef, rows, diag, tally);
            }
            if (IsVector(left) && IsVector(right))
            {
                return CompareVectors(ToDoubles(left), ToDoubles(right), fieldPath, dimension, operationRef, rows, diag);
            }

            // 枚举串/布尔：严格相等（§3.4）
            outcome.IsMatch = Equals(left, right);
            if (!outcome.IsMatch)
            {
                outcome.Detail = string.Format("枚举/布尔值不一致：{0} vs {1}", Stringify(left), Stringify(right));
                Emit(outcome, fieldPath, dimension, operationRef, left, right, null, null, rows);
            }
            return outcome;
        }

        private static CompareOutcome CompareNumbers(
            double left, double right,
            string fieldPath, string dimension, string operationRef,
            List<DeviationEntry> rows, DiagnosticsCollector diag, CompareTally tally)
        {
            var spec = ToleranceRegistry.Default;
            if (!ToleranceRegistry.TryLookup(fieldPath, out spec))
            {
                diag.Warning("TOLERANCE_UNKNOWN_FIELD",
                    string.Format("字段 {0} 未入容差表，按严格相等判定（plan-comparer.md §3.4 保守默认）", fieldPath));
            }

            var outcome = new CompareOutcome();
            var delta = right - left;
            var match = false;
            var tolerance = 0.0;
            switch (spec.Kind)
            {
                case ToleranceKind.Exact:
                    match = delta == 0;
                    tolerance = 0;
                    break;
                case ToleranceKind.AbsoluteMm:
                    tolerance = spec.Value;
                    match = Math.Abs(delta) <= spec.Value;
                    break;
                case ToleranceKind.RelativePercent:
                    var max = Math.Max(Math.Abs(left), Math.Abs(right));
                    var r = max == 0 ? 0 : Math.Abs(delta) / max;   // 双零 → 0（§2.3）
                    tolerance = spec.Value / 100.0;
                    match = r <= tolerance;
                    delta = r * Math.Sign(delta);                   // 相对口径：delta = 有符号相对差（§3.2）
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            outcome.IsMatch = match;
            outcome.Delta = delta;
            outcome.Tolerance = tolerance;

            // 评分口径：仅 parameter/strategy/tool 的容差口径数值字段计入 param_deviation_mean（§4.6）
            if (IsScoreDimension(dimension) && (spec.Kind == ToleranceKind.AbsoluteMm || spec.Kind == ToleranceKind.RelativePercent))
            {
                var max2 = Math.Max(Math.Abs(left), Math.Abs(right));
                tally.ParamRelativeDeviations.Add(max2 == 0 ? 0 : Math.Abs(right - left) / max2);
            }

            if (!match)
            {
                outcome.Detail = string.Format("数值不一致：left {0} vs right {1}（{2} 口径）", FormatNum(left), FormatNum(right), spec.Kind);
                Emit(outcome, fieldPath, dimension, operationRef, left, right, delta, tolerance, rows);
            }
            return outcome;
        }

        private static CompareOutcome CompareVectors(
            List<double> left, List<double> right,
            string fieldPath, string dimension, string operationRef,
            List<DeviationEntry> rows, DiagnosticsCollector diag)
        {
            var outcome = new CompareOutcome();
            if (left.Count != right.Count)
            {
                outcome.IsMatch = false;
                outcome.Detail = string.Format("向量长度不同：{0} vs {1}", left.Count, right.Count);
                Emit(outcome, fieldPath, dimension, operationRef, left, right, null, null, rows);
                return outcome;
            }

            var spec = ToleranceRegistry.Default;
            if (!ToleranceRegistry.TryLookup(fieldPath, out spec))
            {
                diag.Warning("TOLERANCE_UNKNOWN_FIELD",
                    string.Format("字段 {0} 未入容差表，按逐分量严格相等判定（plan-comparer.md §3.4 保守默认）", fieldPath));
            }

            if (spec.Kind == ToleranceKind.VectorMm)
            {
                var dist = Distance(left, right);
                outcome.IsMatch = dist <= spec.Value;
                outcome.Delta = dist;
                outcome.Tolerance = spec.Value;
                if (!outcome.IsMatch)
                {
                    outcome.Detail = string.Format("向量欧氏距离 {0} 超容差 {1}（plan-comparer.md §3.4）", FormatNum(dist), spec.Value);
                    Emit(outcome, fieldPath, dimension, operationRef, left, right, dist, spec.Value, rows);
                }
                return outcome;
            }

            // 未入表（或非 VectorMm 口径）的向量：逐分量严格相等
            outcome.IsMatch = left.SequenceEqual(right);
            if (!outcome.IsMatch)
            {
                outcome.Detail = "向量逐分量不一致（严格相等口径）";
                Emit(outcome, fieldPath, dimension, operationRef, left, right, null, null, rows);
            }
            return outcome;
        }

        private static void Emit(
            CompareOutcome outcome, string fieldPath, string dimension, string operationRef,
            object left, object right, double? delta, double? tolerance,
            List<DeviationEntry> rows)
        {
            rows.Add(new DeviationEntry
            {
                Dimension = dimension,
                OperationRef = operationRef,
                Field = fieldPath,
                Kind = DeviationKinds.Deviation,
                Severity = DiagnosticsCollector.LevelWarning,
                Left = left,
                Right = right,
                Delta = delta,
                Tolerance = tolerance,
                Detail = outcome.Detail,
            });
        }

        private static bool IsScoreDimension(string dimension)
        {
            return dimension == ReportDimensions.Parameter
                || dimension == ReportDimensions.Strategy
                || dimension == ReportDimensions.Tool;
        }

        // ---- 归一与类型判定 ----

        /// <summary>JToken（反序列化值）→ 纯 CLR（double/string/bool/List/Dictionary），fixtures 的原始值原样返回。</summary>
        public static object Normalize(object value)
        {
            var token = value as JToken;
            if (token == null)
            {
                return value;
            }
            switch (token.Type)
            {
                case JTokenType.Object:
                    return ((JObject)token).Properties().ToDictionary(p => p.Name, p => Normalize(p.Value), StringComparer.Ordinal);
                case JTokenType.Array:
                    return ((JArray)token).Select(Normalize).ToList();
                default:
                    return ((JValue)token).Value;
            }
        }

        /// <summary>两数值向量的欧氏距离（VectorMm 口径）。</summary>
        public static double Distance(IList<double> a, IList<double> b)
        {
            var sum = 0.0;
            for (var i = 0; i < a.Count; i++)
            {
                var d = a[i] - b[i];
                sum += d * d;
            }
            return Math.Sqrt(sum);
        }

        private static bool IsNumber(object value)
        {
            return value is byte || value is short || value is int || value is long
                || value is float || value is double || value is decimal;
        }

        private static bool IsVector(object value)
        {
            var list = value as IList;
            return list != null && list.Count > 0 && list.Cast<object>().All(IsNumber);
        }

        private static double ToDouble(object value)
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        private static List<double> ToDoubles(object value)
        {
            return ((IList)value).Cast<object>().Select(ToDouble).ToList();
        }

        private static string Stringify(object value)
        {
            return value == null ? "<null>" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static string FormatNum(double value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
