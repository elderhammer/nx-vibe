using System.Linq;
using Autocam.Plan.Core.Plan;
using Autocam.PlanComparer.Core.Compare;
using Autocam.PlanComparer.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanComparer.Core.Tests
{
    public class AlignmentTests
    {
        // 性质：§3.3 对齐保真——纯置换：全配成功，仅 order_swap 行，参数/刀具/几何零幽灵偏差。
        // 依据：plan-comparer.md §3.3 / §4.2
        // 失败含义：配错对 → 幽灵偏差淹没真实差异，闭环误报。
        [Fact]
        public void Permutation_produces_only_order_rows()
        {
            var left = ComparerFixtures.TwoOpPlan();
            var right = ComparerFixtures.TwoOpPlan();
            // 交换 right 的两个叶子（MILL_X 在前）
            var program1 = right.Workplan.Root.Children[0];
            var leaf = program1.Children[1];
            program1.Children.RemoveAt(1);
            program1.Children.Insert(0, leaf);

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            Assert.Equal(2, ComparerFixtures.Rows(report, "structure", "order_swap").Length);
            Assert.Empty(ComparerFixtures.Rows(report, "parameter"));
            Assert.Empty(ComparerFixtures.Rows(report, "strategy"));
            Assert.Empty(ComparerFixtures.Rows(report, "tool"));
            Assert.Empty(ComparerFixtures.Rows(report, "geometry"));
            Assert.Equal(2, report.Summary.Structure.MatchedOps);
            Assert.Equal(2, report.Summary.Structure.OrderSwaps);
            Assert.Equal(1.0, report.Scores.StructureConsistency);
        }

        // 性质：§3.3——插入（right 多一道工序）→ 仅 extra 行，其余配对不受影响。
        // 依据：plan-comparer.md §3.3
        // 失败含义：插入破坏既有配对 → 既有工序被误报参数偏差。
        [Fact]
        public void Insertion_produces_extra_row_only()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            // right 追加第二道 drill 工序（新 ID、新引用链）
            var op2 = new OperationEntry { OperationId = "OP-2", OperationType = "drill", ToolRef = "T-1", NxTemplate = new NxTemplateEntry { Type = "DRILL", Subtype = "" } };
            op2.Strategy["cycle"] = "PECK";
            var ws2 = new WorkingstepEntry { WorkingstepId = "WS-2", OperationRef = "OP-2", FeatureRef = "F-1", SetupRef = "SET-1" };
            var leaf2 = new WorkplanNodeEntry { Name = "DRILL_2", WorkingstepRef = "WS-2" };
            right.Workplan.Root.Children[0].Children.Add(leaf2);
            right.Workplan.Elements.Add(leaf2);
            right.Operations.Add(op2);
            right.Workingsteps.Add(ws2);

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            Assert.Single(ComparerFixtures.Rows(report, "structure", "extra"));
            Assert.Empty(ComparerFixtures.Rows(report, "structure", "missing"));
            Assert.Empty(ComparerFixtures.Rows(report, "strategy"));
            Assert.Equal(1, report.Summary.Structure.MatchedOps);
            Assert.Equal(2, report.Summary.Structure.TotalOps);
        }

        // 性质：§3.3——类型替换（同位置、无法配对）→ type_mismatch 行，不做字段比较。
        // 依据：plan-comparer.md §3.3 / §4.2
        // 失败含义：类型不同的工序被硬配 → 字段比较毫无意义，噪声淹没类型错本身。
        [Fact]
        public void Type_change_produces_type_mismatch_not_field_deviations()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Operations[0].OperationType = "mill_cavity";
            right.Operations[0].NxTemplate.Type = "CAVITY_MILL";

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            var row = ComparerFixtures.Rows(report, "structure", "type_mismatch").Single();
            Assert.Equal("OP-1", row.OperationRef);
            Assert.Empty(ComparerFixtures.Rows(report, "strategy"));
            Assert.Empty(ComparerFixtures.Rows(report, "parameter"));
            Assert.Empty(ComparerFixtures.Rows(report, "tool"));
            Assert.Equal(0, report.Summary.Structure.MatchedOps);
            Assert.Equal(1, report.Summary.Structure.TypeMismatches);
        }

        // 性质：§3.3——多重集不等：贪心配对（right 序取最早可用同键 left，匹配数 = LCS 最大长度），
        //       残留按位置规则落 missing + extra 而非硬配。
        // 依据：plan-comparer.md §4.2（贪心决胜：right 序 + left 最早）
        // 失败含义：决胜不定 → 报告不可复现（§3.6 确定性崩塌）。
        [Fact]
        public void Multiset_difference_pairs_greedily_and_flags_missing_extra()
        {
            var left = ComparerFixtures.TwoOpPlan();           // [drill, mill_cavity]
            var right = ComparerFixtures.TwoOpPlan();
            right.Operations[0].OperationType = "mill_cavity"; // right = [mill_cavity, mill_cavity]
            right.Operations[0].NxTemplate.Type = "CAVITY_MILL";

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            // 贪心：right mill_cavity@0 ↔ left mill_cavity@1；left drill 落 missing、right 另一 mill_cavity 落 extra
            Assert.Single(ComparerFixtures.Rows(report, "structure", "missing"));
            Assert.Single(ComparerFixtures.Rows(report, "structure", "extra"));
            Assert.Equal(1, report.Summary.Structure.MatchedOps);
        }

        // 性质：§4.3 组树对比——组改名 → deviation 行（operation_ref 为空）；组增删 → missing/extra 组行。
        // 依据：plan-comparer.md §4.3
        // 失败含义：组结构差异静默，workplan 层级被污染而报告看不出。
        [Fact]
        public void Group_tree_diff_reports_rename_and_count()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Workplan.Root.Children[0].Name = "PROGRAM_2";   // 改名

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            var rename = ComparerFixtures.Rows(report, "structure", "deviation").Single();
            Assert.Null(rename.OperationRef);
            Assert.Equal("PROGRAM_1", rename.Left);
            Assert.Equal("PROGRAM_2", rename.Right);
            Assert.True(report.Summary.Structure.GroupDiffs >= 1);
        }

        // 性质：§3.10-4 引用悬空——叶子引用链断 → unaligned 行 + error 诊断，不终止。
        // 依据：plan-comparer.md §3.10-4 / §4.2
        // 失败含义：悬空引用被静默跳过或硬配，闭环看不到「哪片叶子没对上」。
        [Fact]
        public void Dangling_leaf_produces_unaligned_row_and_error()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Workplan.Root.Children[0].Children[0].WorkingstepRef = "WS-999";   // 悬空

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            Assert.Single(ComparerFixtures.Rows(report, "structure", "unaligned"));
            Assert.Contains(report.Diagnostics, d => d.Level == "ERROR");
            Assert.Empty(ComparerFixtures.Rows(report, "strategy"));
        }
    }
}
