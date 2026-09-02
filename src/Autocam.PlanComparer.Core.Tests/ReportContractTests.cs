using System.Linq;
using Autocam.PlanComparer.Core.Compare;
using Autocam.PlanComparer.Core.Report;
using Autocam.PlanComparer.Core.Tests.TestDoubles;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Autocam.PlanComparer.Core.Tests
{
    public class ReportContractTests
    {
        // 性质：§3.11-1 后置条件——零偏差报告通过 autocam-compare-report.schema.json 校验。
        // 依据：plan-comparer.md §3.11-1
        // 失败含义：报告形状破坏合同 → 报告页无法解析。
        [Fact]
        public void Zero_deviation_report_passes_schema()
        {
            var plan = ComparerFixtures.OneOpPlan();
            var report = PlanComparePipeline.Compare(plan, plan, ComparerFixtures.Context());

            var errors = ReportSchemaAsset.Validate(ReportSerializer.Serialize(report));

            Assert.Empty(errors);
        }

        // 性质：§3.11-1——含偏差行（null 字段/JToken 值/数值 delta）的报告同样过校验。
        // 依据：plan-comparer.md §3.11-1
        // 失败含义：真实偏差报告过不了自己的合同 → 后置条件自杀（反面清单 #4 类问题）。
        [Fact]
        public void Deviation_report_passes_schema()
        {
            var left = ComparerFixtures.TwoOpPlan();
            var right = ComparerFixtures.TwoOpPlan();
            right.Resources.Tools[0].Diameter = 7.0;                       // 数值偏差（delta/tolerance，DRILL 刀）
            right.Operations[0].Strategy["cycle"] = "DRILL";               // 枚举偏差（无 delta）
            right.Operations[0].Technology.Remove("spindle_rpm");          // missing 行
            right.Features[0].GeometryRef = null;                          // geometry missing 行
            right.Workplan.Root.Children[0].Children[1].WorkingstepRef = "WS-999"; // unaligned 行（MILL 叶，不影响 drill 配对）

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());
            var errors = ReportSchemaAsset.Validate(ReportSerializer.Serialize(report));

            Assert.Empty(errors);
            Assert.True(report.Deviations.Count >= 5);
        }

        // 性质：§3.6 序列化口径——snake_case 键、null 省略（与 plan 序列化同策略）。
        // 依据：plan-comparer.md §3.6 / ReportSerializer
        // 失败含义：键名风格漂移 → 报告页与 plan 双合同解析不一致。
        [Fact]
        public void Report_json_uses_snake_case_and_omits_nulls()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Operations[0].Strategy["cycle"] = "DRILL";   // 枚举偏差行：delta/tolerance 为 null

            var json = ReportSerializer.Serialize(PlanComparePipeline.Compare(left, right, ComparerFixtures.Context()));
            var root = JObject.Parse(json);
            var row = (JObject)root["deviations"].First();

            Assert.NotNull(root["report_id"]);
            Assert.Null(root["reportId"]);
            Assert.NotNull(row["operation_ref"]);
            Assert.Null(row["operationRef"]);
            Assert.Null(row["delta"]);          // null 省略
            Assert.Null(row["tolerance"]);      // null 省略
            Assert.Null(root["toolpath"]);      // 预留通道 null 省略
        }
    }
}
