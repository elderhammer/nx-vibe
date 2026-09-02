using System.Linq;
using Autocam.PlanExecutor.Core.Build;
using Autocam.PlanExecutor.Core.Tests.TestDoubles;
using Autocam.PlanExporter.Core.Plan;
using Autocam.PlanExporter.Core.Tests.TestDoubles;
using Newtonsoft.Json;
using Xunit;

namespace Autocam.PlanExecutor.Core.Tests
{
    public class RoundTripTests
    {
        // 性质：§3.1a round-trip（核心）——完整 plan 重建后再导出，逐字段等价（ID 按位置归一）。
        // 依据：plan-executor.md §3.1a
        // 失败含义：合同存在歧义——「按 plan 重建」与「按 plan 导出」不自洽，闭环第②③步无意义。
        [Fact]
        public void Complete_plan_round_trips_field_by_field()
        {
            var plan1 = ExecutorFixtures.FullPlan();
            var result = PlanExecutorPipeline.Build(plan1, PlanFixtures.FullCapability());
            var plan2 = PlanFixtures.Export(result.Simulated);

            AssertPlansEquivalent(plan1, plan2);
        }

        // 性质：§3.1b——稀疏 plan 重建后再导出，字段集相同：缺的仍缺、present 的逐字段相等
        //       （继承语义"不伪造值"的必然推论）。
        // 依据：plan-executor.md §3.1b / §3.3
        // 失败含义：重建侧伪造/丢失字段，继承语义下与 ground truth 产生不可见偏差。
        [Fact]
        public void Sparse_plan_round_trips_with_same_field_set()
        {
            var plan1 = ExecutorFixtures.SparseDrillPlan();
            var result = PlanExecutorPipeline.Build(plan1, PlanFixtures.FullCapability());
            var plan2 = PlanFixtures.Export(result.Simulated);

            var op2 = plan2.Operations.Single();
            // 字段集完全相同：只有 cycle，值相等
            Assert.Equal(new[] { "cycle" }, op2.Strategy.Keys.ToArray());
            Assert.Equal("PECK", op2.Strategy["cycle"]);
            Assert.Empty(op2.Technology);
            // 刀具字段集：只有 diameter
            Assert.Equal(new[] { "diameter" }, FieldsOf(plan2.Resources.Tools.Single()));
            Assert.Equal(6.8, (double)plan2.Resources.Tools.Single().Diameter);
            // 无 feature 几何 → geometry_ref 缺省
            Assert.Null(plan2.Features.Single().GeometryRef);
        }

        // 性质：§3.2 确定性——同 plan 两次 Build，命令序列字节级相同。
        // 依据：plan-executor.md §3.2
        // 失败含义：存在不确定源，PlanComparer 对齐与适配层重放无法复现。
        [Fact]
        public void Build_is_deterministic()
        {
            var plan = ExecutorFixtures.FullPlan();
            var first = JsonConvert.SerializeObject(PlanExecutorPipeline.Build(plan, PlanFixtures.FullCapability()).Commands);
            var second = JsonConvert.SerializeObject(PlanExecutorPipeline.Build(plan, PlanFixtures.FullCapability()).Commands);

            Assert.Equal(first, second);
        }

        /// <summary>round-trip 等价断言（ID 按位置归一：两边同序产出）。</summary>
        private static void AssertPlansEquivalent(PlanRoot plan1, PlanRoot plan2)
        {
            Assert.Equal(plan1.PlanId, plan2.PlanId);
            Assert.Equal(plan1.Name, plan2.Name);
            Assert.Equal(plan1.InputRef, plan2.InputRef);

            Assert.Equal(plan1.Operations.Count, plan2.Operations.Count);
            for (var i = 0; i < plan1.Operations.Count; i++)
            {
                var a = plan1.Operations[i];
                var b = plan2.Operations[i];
                Assert.Equal(a.OperationType, b.OperationType);
                Assert.Equal(a.NxTemplate.Type, b.NxTemplate.Type);
                Assert.Equal(a.NxTemplate.Subtype, b.NxTemplate.Subtype);
                Assert.Equal(a.ToolRef, b.ToolRef);
                Assert.Equal(JsonConvert.SerializeObject(a.Strategy), JsonConvert.SerializeObject(b.Strategy));
                Assert.Equal(JsonConvert.SerializeObject(a.Technology), JsonConvert.SerializeObject(b.Technology));
            }

            Assert.Equal(plan1.Resources.Tools.Count, plan2.Resources.Tools.Count);
            for (var i = 0; i < plan1.Resources.Tools.Count; i++)
            {
                Assert.Equal(JsonConvert.SerializeObject(plan1.Resources.Tools[i]), JsonConvert.SerializeObject(plan2.Resources.Tools[i]));
            }

            Assert.Equal(plan1.Setups.Count, plan2.Setups.Count);
            for (var i = 0; i < plan1.Setups.Count; i++)
            {
                Assert.Equal(JsonConvert.SerializeObject(plan1.Setups[i]), JsonConvert.SerializeObject(plan2.Setups[i]));
            }

            Assert.Equal(plan1.Features.Count, plan2.Features.Count);
            for (var i = 0; i < plan1.Features.Count; i++)
            {
                var a = plan1.Features[i].GeometryRef?.AnchorPoint;
                var b = plan2.Features[i].GeometryRef?.AnchorPoint;
                Assert.Equal(a, b);
            }

            // 工步引用位置对齐：两边第 i 个工步 → 第 i 个工序/特征/setup
            Assert.Equal(plan1.Workingsteps.Count, plan2.Workingsteps.Count);
            for (var i = 0; i < plan1.Workingsteps.Count; i++)
            {
                Assert.Equal(plan2.Workingsteps[i].OperationRef, plan2.Operations[i].OperationId);
                Assert.Equal(plan2.Workingsteps[i].FeatureRef, plan2.Features[i].FeatureId);
                Assert.Equal(plan2.Workingsteps[i].SetupRef, plan2.Setups[0].SetupId);
                Assert.Equal(plan1.Workingsteps[i].OperationRef, plan1.Operations[i].OperationId);
            }

            // workplan：前序名序列相同；叶子 workingstep_ref 位置对齐
            Assert.Equal(
                plan1.Workplan.Elements.Select(e => e.Name).ToArray(),
                plan2.Workplan.Elements.Select(e => e.Name).ToArray());
            var leaves2 = plan2.Workplan.Elements.Where(e => e.WorkingstepRef != null).ToList();
            Assert.Equal(plan2.Workingsteps.Count, leaves2.Count);
            for (var i = 0; i < leaves2.Count; i++)
            {
                Assert.Equal(plan2.Workingsteps[i].WorkingstepId, leaves2[i].WorkingstepRef);
            }
        }

        private static string[] FieldsOf(ToolEntry tool)
        {
            var fields = new System.Collections.Generic.List<string>();
            if (tool.Type != null) fields.Add("type");
            if (tool.Diameter != null) fields.Add("diameter");
            if (tool.NumFlutes != null) fields.Add("num_flutes");
            if (tool.FluteLength != null) fields.Add("flute_length");
            if (tool.LowerCornerRadius != null) fields.Add("lower_corner_radius");
            return fields.ToArray();
        }
    }
}
