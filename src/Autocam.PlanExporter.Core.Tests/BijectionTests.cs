using System.Collections.Generic;
using System.Linq;
using Autocam.PlanExporter.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanExporter.Core.Tests
{
    public class BijectionTests
    {
        // 性质：后置条件 5（§3.3-5）——每个 Operation ↔ 恰好一个 operation 条目 + 一个
        //       workingstep 条目 + 一个 feature 条目，无孤儿、无重复。
        // 依据：plan-exporter.md §3.3-5
        // 失败含义：闭环第②③步按条目对齐时出现错配或缺口。
        [Fact]
        public void Export_counts_match_input_ops()
        {
            var plan = PlanFixtures.ExportDefault();   // 夹具含 2 道工序

            Assert.Equal(2, plan.Operations.Count);
            Assert.Equal(2, plan.Workingsteps.Count);
            Assert.Equal(2, plan.Features.Count);
        }

        // 性质：后置条件 5——工序 ↔ workingstep/feature 是一一对应（不重不漏）。
        // 依据：plan-exporter.md §3.3-5
        // 失败含义：一工序多条目或多工序一条目都会使对比对齐失效。
        [Fact]
        public void Every_operation_has_exactly_one_workingstep_and_feature()
        {
            var plan = PlanFixtures.ExportDefault();

            Assert.Equal(2, plan.Workingsteps.Select(w => w.OperationRef).Distinct().Count());
            Assert.Equal(2, plan.Workingsteps.Select(w => w.FeatureRef).Distinct().Count());
            foreach (var ws in plan.Workingsteps)
            {
                Assert.NotNull(ws.OperationRef);
                Assert.NotNull(ws.FeatureRef);
            }
        }

        // 性质：后置条件 5 反向——无孤儿条目：每条 entry 都能被引用链追溯回某道工序；
        //       workplan 叶子引用数 = 工序数且无重复。
        // 依据：plan-exporter.md §3.3-5
        // 失败含义：孤儿条目（如未被引用的 feature/tool）会让对比出现"多"类偏差噪声。
        [Fact]
        public void No_orphan_entries()
        {
            var plan = PlanFixtures.ExportDefault();

            var referencedFeatures = new HashSet<string>(plan.Workingsteps.Select(w => w.FeatureRef));
            var referencedOps = new HashSet<string>(plan.Workingsteps.Select(w => w.OperationRef));
            var referencedTools = new HashSet<string>(plan.Operations.Select(o => o.ToolRef));
            var referencedSetups = new HashSet<string>(plan.Workingsteps.Select(w => w.SetupRef));

            Assert.All(plan.Features, f => Assert.Contains(f.FeatureId, referencedFeatures));
            Assert.All(plan.Operations, o => Assert.Contains(o.OperationId, referencedOps));
            Assert.All(plan.Resources.Tools, t => Assert.Contains(t.ToolId, referencedTools));
            Assert.All(plan.Setups, s => Assert.Contains(s.SetupId, referencedSetups));

            var leafRefs = plan.Workplan.Elements.Where(e => e.WorkingstepRef != null).Select(e => e.WorkingstepRef).ToList();
            Assert.Equal(plan.Workingsteps.Count, leafRefs.Count);
            Assert.Equal(leafRefs.Count, leafRefs.Distinct().Count());
        }

        // 性质：I3——ID 全局唯一且确定（单调递增计数器）：跨所有 id 空间无重复。
        // 依据：plan-exporter.md §3.4 I3
        // 失败含义：ID 撞车使引用闭合形同虚设，PlanComparer 按 id 对齐时错配。
        [Fact]
        public void Ids_are_globally_unique()
        {
            var plan = PlanFixtures.ExportDefault();

            var ids = new List<string> { plan.PlanId };
            ids.AddRange(plan.Setups.Select(s => s.SetupId));
            ids.AddRange(plan.Resources.Tools.Select(t => t.ToolId));
            ids.AddRange(plan.Features.Select(f => f.FeatureId));
            ids.AddRange(plan.Operations.Select(o => o.OperationId));
            ids.AddRange(plan.Workingsteps.Select(w => w.WorkingstepId));

            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.All(ids, id => Assert.False(string.IsNullOrEmpty(id)));
        }
    }
}
