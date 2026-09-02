using System.Linq;
using Autocam.PlanExporter.Core.Plan;
using Autocam.PlanExporter.Core.Serialization;
using Autocam.PlanExporter.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanExporter.Core.Tests
{
    public class SchemaContractTests
    {
        // 性质：后置条件 1（plan-exporter.md §3.3-1）——导出产物必须通过 autocam-plan.schema.json v3 校验。
        // 依据：plan-exporter.md §3.3-1 / §4.7-5
        // 失败含义：导出器产出了合同外数据，PlanExecutor / 云端 / PlanComparer 任一消费端将无法解析该 plan。
        [Fact]
        public void Default_export_passes_schema_v3()
        {
            var plan = PlanFixtures.ExportDefault();
            var json = PlanSerializer.Serialize(plan);

            Assert.Empty(SchemaAsset.Validate(json));
        }

        // 性质：后置条件 2 + §3.1d——MVP 字段读不到时绝不静默省略：缺项必须显式落 diagnostics。
        // 依据：plan-exporter.md §3.3-2 / §3.1d
        // 失败含义：某字段静默丢失时，重建侧会继承组默认值，与 ground truth 产生不可见偏差。
        [Fact]
        public void Unresolvable_params_yield_warning_diagnostic()
        {
            var plan = PlanFixtures.ExportDefault();

            var warnings = plan.Diagnostics.Where(d => d.Level == "WARNING").ToList();
            Assert.NotEmpty(warnings);
            Assert.Contains(warnings, d => d.Detail.Contains("cut_order"));
        }

        // 性质：schema 可选政策——必填最小集（plan_id/operations/workingsteps/workplan）之外全部可选，
        //       最小 plan 依然校验通过（对应"导出全量、导入允许缺省继承"的单一合同）。
        // 依据：nx-plugin-design.md §5 注释 / schema $comment
        // 失败含义：schema 必填集被扩大，与导入侧缺省继承语义冲突。
        [Fact]
        public void Minimal_plan_is_schema_valid()
        {
            var minimal = new PlanRoot
            {
                PlanId = "PLAN-MIN",
                Workplan = new WorkplanEntry { Root = new WorkplanNodeEntry() },
            };

            var json = PlanSerializer.Serialize(minimal);

            Assert.Empty(SchemaAsset.Validate(json));
        }

        // 性质：默认夹具（健康的完整工程）导出无 ERROR 级诊断——ERROR 只属于真实的失败路径。
        // 依据：plan-exporter.md §3.2（前置条件 1-7 对健康输入全部满足）
        // 失败含义：健康路径被误报为错误，闭环第②③步会误判 ground truth 工程有问题。
        [Fact]
        public void Default_export_has_no_error_diagnostics()
        {
            var plan = PlanFixtures.ExportDefault();

            Assert.DoesNotContain(plan.Diagnostics, d => d.Level == "ERROR");
        }
    }
}
