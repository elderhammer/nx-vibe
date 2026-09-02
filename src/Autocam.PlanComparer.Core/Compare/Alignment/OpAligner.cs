using System;
using System.Collections.Generic;
using System.Linq;
using Autocam.Plan.Core.Diagnostics;
using Autocam.PlanComparer.Core.Report;

namespace Autocam.PlanComparer.Core.Compare.Alignment
{
    /// <summary>配对成功的工序对——七维度字段比较的唯一入口（配对纪律 §3.3 的结构性保证）。</summary>
    public sealed class OpPair
    {
        public LeafEntry Left { get; set; }
        public LeafEntry Right { get; set; }
    }

    /// <summary>
    /// 工序对齐（plan-comparer.md §4.2，核心算法）：
    /// 1. 类型多重集相等（key = operation_type|nx_template.type）→ 实例序配对（全配成功），
    ///    配对位置不同 → 每工序一条 order_swap 行；
    /// 2. 多重集不等 → 贪心配对：right 按序取最早可用同键 left（同键内实例可互换，
    ///    匹配数 = Σ min(两侧各键计数) = LCS 最大长度；决胜 = right 序 + left 最早）；
    ///    未配对残留：位置相同且双方均未配对 → type_mismatch 行（不做字段比较），
    ///    其余 → missing（left 独有）/ extra（right 独有）行。
    /// 未配对工序绝不产生参数/刀具/几何偏差行（§3.3 对齐保真）。
    /// </summary>
    public static class OpAligner
    {
        public static List<OpPair> Align(
            SideModel left,
            SideModel right,
            List<DeviationEntry> rows,
            DiagnosticsCollector diag,
            CompareTally tally)
        {
            var lLeaves = left.Leaves;
            var rLeaves = right.Leaves;
            tally.TotalOps = Math.Max(lLeaves.Count, rLeaves.Count);

            var pairs = MultisetsEqual(lLeaves, rLeaves)
                ? PairByInstanceOrder(lLeaves, rLeaves, rows)
                : PairGreedy(lLeaves, rLeaves, rows);

            tally.MatchedOps = pairs.Count;
            return pairs;
        }

        /// <summary>类型键（§4.2）：operation_type + nx_template.type。</summary>
        private static string Key(LeafEntry leaf)
        {
            return leaf.Op.OperationType + "|" + (leaf.Op.NxTemplate?.Type ?? "");
        }

        private static bool MultisetsEqual(List<LeafEntry> lLeaves, List<LeafEntry> rLeaves)
        {
            if (lLeaves.Count != rLeaves.Count)
            {
                return false;
            }
            var lCounts = lLeaves.GroupBy(Key).ToDictionary(g => g.Key, g => g.Count());
            foreach (var g in rLeaves.GroupBy(Key))
            {
                if (!lCounts.TryGetValue(g.Key, out var count) || count != g.Count())
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>多重集相等：每种 key 按出现序一一配对；位置不同 → order_swap 行。</summary>
        private static List<OpPair> PairByInstanceOrder(List<LeafEntry> lLeaves, List<LeafEntry> rLeaves, List<DeviationEntry> rows)
        {
            var queues = new Dictionary<string, Queue<LeafEntry>>();
            var lPos = lLeaves.Select((leaf, i) => new { leaf, i }).ToDictionary(x => x.leaf, x => x.i);
            foreach (var l in lLeaves)
            {
                if (!queues.TryGetValue(Key(l), out var q))
                {
                    queues[Key(l)] = q = new Queue<LeafEntry>();
                }
                q.Enqueue(l);
            }

            var pairs = new List<OpPair>();
            for (var rIdx = 0; rIdx < rLeaves.Count; rIdx++)
            {
                var r = rLeaves[rIdx];
                var l = queues[Key(r)].Dequeue();
                pairs.Add(new OpPair { Left = l, Right = r });
                if (lPos[l] != rIdx)
                {
                    rows.Add(new DeviationEntry
                    {
                        Dimension = ReportDimensions.Structure,
                        OperationRef = l.Op.OperationId,
                        Field = "position",
                        Kind = DeviationKinds.OrderSwap,
                        Severity = DiagnosticsCollector.LevelWarning,
                        Left = lPos[l] + 1,      // 1 基序号（§4.2）
                        Right = rIdx + 1,
                        Detail = string.Format("工序 {0} 输出顺序不同：left 第 {1} 位 vs right 第 {2} 位（plan-comparer.md §4.2）",
                            l.Op.OperationId, lPos[l] + 1, rIdx + 1),
                    });
                }
            }
            return pairs;
        }

        /// <summary>多重集不等：贪心配对（right 序取最早可用同键 left）+ 未配对残留处置（同位置 type_mismatch，其余 missing/extra）。</summary>
        private static List<OpPair> PairGreedy(List<LeafEntry> lLeaves, List<LeafEntry> rLeaves, List<DeviationEntry> rows)
        {
            var n = lLeaves.Count;
            var m = rLeaves.Count;
            var pairs = new List<OpPair>();
            var lMatched = new bool[n];
            var rMatched = new bool[m];

            // right 按序，每个取最早可用同键 left（§4.2 决胜）
            for (var j = 0; j < m; j++)
            {
                for (var i = 0; i < n; i++)
                {
                    if (!lMatched[i] && Key(lLeaves[i]) == Key(rLeaves[j]))
                    {
                        pairs.Add(new OpPair { Left = lLeaves[i], Right = rLeaves[j] });
                        lMatched[i] = true;
                        rMatched[j] = true;
                        break;
                    }
                }
            }

            // 未配对残留：同位置双方均未配对 → type_mismatch；其余 → missing/extra
            var consumed = new bool[Math.Max(n, m)];
            var min = Math.Min(n, m);
            for (var i = 0; i < min; i++)
            {
                if (!lMatched[i] && !rMatched[i])
                {
                    consumed[i] = true;
                    var l = lLeaves[i];
                    var r = rLeaves[i];
                    rows.Add(new DeviationEntry
                    {
                        Dimension = ReportDimensions.Structure,
                        OperationRef = l.Op.OperationId,
                        Kind = DeviationKinds.TypeMismatch,
                        Severity = DiagnosticsCollector.LevelWarning,
                        Left = Key(l),
                        Right = Key(r),
                        Detail = string.Format("工序 {0} 类型不同（left {1} vs right {2}），不配对比（plan-comparer.md §4.2）",
                            l.Op.OperationId, Key(l), Key(r)),
                    });
                }
            }
            for (var i = 0; i < n; i++)
            {
                if (!lMatched[i] && !(i < min && consumed[i]))
                {
                    rows.Add(new DeviationEntry
                    {
                        Dimension = ReportDimensions.Structure,
                        OperationRef = lLeaves[i].Op.OperationId,
                        Kind = DeviationKinds.Missing,
                        Severity = DiagnosticsCollector.LevelWarning,
                        Detail = string.Format("工序 {0}（{1}）在 right 侧缺失（plan-comparer.md §4.2）",
                            lLeaves[i].Op.OperationId, Key(lLeaves[i])),
                    });
                }
            }
            for (var j = 0; j < m; j++)
            {
                if (!rMatched[j] && !(j < min && consumed[j]))
                {
                    rows.Add(new DeviationEntry
                    {
                        Dimension = ReportDimensions.Structure,
                        Kind = DeviationKinds.Extra,
                        Severity = DiagnosticsCollector.LevelWarning,
                        Field = Key(rLeaves[j]),
                        Detail = string.Format("right 侧多出工序（{0}），left 无对应（plan-comparer.md §4.2）", Key(rLeaves[j])),
                    });
                }
            }
            return pairs;
        }
    }
}
