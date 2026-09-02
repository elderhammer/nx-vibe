using Autocam.Plan.Core.Plan;
using Autocam.PlanComparer.Core.Compare;
using Autocam.PlanComparer.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanComparer.Core.Tests
{
    public class PreconditionTests
    {
        // 性质：§3.10-1 前置条件——任一侧 plan 为 null → CompareAbortedException，终止。
        // 依据：plan-comparer.md §3.10-1
        // 失败含义：null 输入被静默处理或空指针崩溃 → 适配层无法区分「对比失败」与「结果为空」。
        [Fact]
        public void Null_plan_aborts()
        {
            var plan = ComparerFixtures.OneOpPlan();

            Assert.Throws<CompareAbortedException>(() => PlanComparePipeline.Compare(null, plan, ComparerFixtures.Context()));
            Assert.Throws<CompareAbortedException>(() => PlanComparePipeline.Compare(plan, null, ComparerFixtures.Context()));
        }

        // 性质：§3.10-2——workplan.root 缺失 → 终止（对齐无权威序）。
        // 依据：plan-comparer.md §3.10-2
        // 失败含义：无 workplan 的 plan 被强行对比 → 对齐结果无意义。
        [Fact]
        public void Missing_workplan_root_aborts()
        {
            var plan = ComparerFixtures.OneOpPlan();
            var broken = ComparerFixtures.OneOpPlan();
            broken.Workplan = new WorkplanEntry();   // 无 root

            Assert.Throws<CompareAbortedException>(() => PlanComparePipeline.Compare(plan, broken, ComparerFixtures.Context()));
        }
    }
}
