using System.Linq;
using Autocam.Plan.Core.Serialization;
using Autocam.PlanComparer.Core.Compare;
using Autocam.PlanComparer.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanComparer.Core.Tests
{
    public class ReflexivityTests
    {
        // 性质：§3.1 自反性——Compare(P, P) = 零偏差报告（验收基线：先证明「同则不差」）。
        // 依据：plan-comparer.md §3.1
        // 失败含义：比较器对相同输入报偏差，闭环比对「差在哪」失去可信度。
        [Fact]
        public void Same_plan_compares_to_zero_deviations()
        {
            var plan = ComparerFixtures.OneOpPlan();
            var report = PlanComparePipeline.Compare(plan, plan, ComparerFixtures.Context());

            Assert.Empty(report.Deviations);
            Assert.Equal(1, report.Summary.Structure.MatchedOps);
            Assert.Equal(1, report.Summary.Structure.TotalOps);
            Assert.Equal(1.0, report.Scores.StructureConsistency);
            Assert.Equal(0.0, report.Scores.ParamDeviationMean);
            Assert.Equal(1.0, report.Scores.GeometryMatchRate);
            Assert.DoesNotContain(report.Diagnostics, d => d.Level == "ERROR");
        }

        // 性质：§3.1——双工序自反：配对覆盖每个工序，各维度零偏差。
        // 依据：plan-comparer.md §3.1
        // 失败含义：多工序下配对或维度比较漏报/误报。
        [Fact]
        public void Two_op_plan_compares_to_zero_deviations()
        {
            var plan = ComparerFixtures.TwoOpPlan();
            var report = PlanComparePipeline.Compare(plan, plan, ComparerFixtures.Context());

            Assert.Empty(report.Deviations);
            Assert.Equal(2, report.Summary.Structure.MatchedOps);
            Assert.Equal(2, report.Summary.Structure.TotalOps);
            Assert.Equal(1.0, report.Scores.StructureConsistency);
        }

        // 性质：§3.10-5——空对空合法：评分 1.0/0.0/1.0（分母 0 → 1.0，空对空视为一致）。
        // 依据：plan-comparer.md §3.10-5 / §2.3
        // 失败含义：空 plan 使评分除零或误报，对比器对退化输入不稳健。
        [Fact]
        public void Empty_plans_score_full_consistency()
        {
            var empty = new Autocam.Plan.Core.Plan.PlanRoot
            {
                PlanId = "PLAN-E",
                Workplan = new Autocam.Plan.Core.Plan.WorkplanEntry
                {
                    Root = new Autocam.Plan.Core.Plan.WorkplanNodeEntry { Name = "PROGRAM" },
                },
            };

            var report = PlanComparePipeline.Compare(empty, empty, ComparerFixtures.Context());

            Assert.Empty(report.Deviations);
            Assert.Equal(1.0, report.Scores.StructureConsistency);
            Assert.Equal(0.0, report.Scores.ParamDeviationMean);
            Assert.Equal(1.0, report.Scores.GeometryMatchRate);
        }

        // 性质：§3.11-5 纯函数——输入 plan 不被修改（生产序列化器前后字节级一致）。
        // 依据：plan-comparer.md §3.11-5
        // 失败含义：比较过程污染输入，重放/再次比较被污染数据，确定性（§3.6）崩塌。
        [Fact]
        public void Compare_does_not_mutate_inputs()
        {
            var left = ComparerFixtures.TwoOpPlan();
            var right = ComparerFixtures.TwoOpPlan();
            right.Operations[1].Technology["spindle_rpm"] = 7000;   // 制造一个偏差

            var leftBefore = PlanSerializer.Serialize(left);
            var rightBefore = PlanSerializer.Serialize(right);
            PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            Assert.Equal(leftBefore, PlanSerializer.Serialize(left));
            Assert.Equal(rightBefore, PlanSerializer.Serialize(right));
        }
    }
}
