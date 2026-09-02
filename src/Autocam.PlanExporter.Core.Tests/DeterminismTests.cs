using System.Linq;
using Autocam.PlanExporter.Core.Serialization;
using Autocam.PlanExporter.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanExporter.Core.Tests
{
    public class DeterminismTests
    {
        // 性质：§3.1e 确定性（幂等退化形式）——同一工程多次导出逐字节一致。
        // 依据：plan-exporter.md §3.1e
        // 失败含义：存在不确定源（未排序集合/字典序/时间/随机），PlanComparer 无法重复执行对比。
        [Fact]
        public void Two_exports_are_byte_identical()
        {
            var first = PlanSerializer.Serialize(PlanFixtures.ExportDefault());
            var second = PlanSerializer.Serialize(PlanFixtures.ExportDefault());

            Assert.Equal(first, second);
        }

        // 性质：§3.1e——plan_id 确定生成（同输入同输出），不依赖时间/随机。
        // 依据：plan-exporter.md §3.1e / §4.7
        // 失败含义：plan_id 漂移会使同一工程的两次导出无法被云端按 id 关联。
        [Fact]
        public void Plan_id_is_deterministic()
        {
            var plan = PlanFixtures.ExportDefault();
            var again = PlanFixtures.ExportDefault();

            Assert.Equal(plan.PlanId, again.PlanId);
        }

        // 性质：§3.1e 遍历序固定——条目顺序 = Program 树前序，不依赖任何集合哈希序。
        // 依据：plan-exporter.md §3.1e / §3.1c
        // 失败含义：条目序漂移时，PlanComparer 按位置对齐会错配工序。
        [Fact]
        public void Entry_order_follows_program_preorder()
        {
            var plan = PlanFixtures.ExportDefault();

            var names = plan.Workplan.Elements.Select(e => e.Name).ToArray();
            Assert.Equal(new[] { "PROGRAM", "PROGRAM_1", "CAVITY_1", "DRILL_1" }, names);
        }
    }
}
