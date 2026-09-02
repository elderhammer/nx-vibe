using System.Linq;
using Autocam.PlanExporter.Core.Serialization;
using Autocam.PlanExporter.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanExporter.Core.Tests
{
    public class DiagnosticsContractTests
    {
        private static readonly string[] Levels = { "INFO", "WARNING", "ERROR" };

        // 性质：§4.7 诊断条目合同——{level ∈ INFO/WARNING/ERROR, code 非空, detail 非空}。
        // 依据：plan-exporter.md §4.7
        // 失败含义：诊断条目畸形会让报告页/云端解析失败，掩盖真正的偏差信息。
        [Fact]
        public void All_diagnostics_have_valid_shape()
        {
            var plan = PlanFixtures.ExportDefault();

            Assert.NotEmpty(plan.Diagnostics);
            foreach (var d in plan.Diagnostics)
            {
                Assert.Contains(d.Level, Levels);
                Assert.False(string.IsNullOrEmpty(d.Code));
                Assert.False(string.IsNullOrEmpty(d.Detail));
            }
        }

        // 性质：§4.7 + 后置条件 1——含全部三种级别的诊断（INFO/WARNING/ERROR）时 plan 仍 schema 合法。
        // 依据：plan-exporter.md §4.7 / §3.3-1
        // 失败含义：诊断字段与 schema 脱节，异常场景的导出无法通过合同校验。
        [Fact]
        public void Mixed_level_diagnostics_are_schema_valid()
        {
            var setup = PlanFixtures.DefaultSetup();
            var profile = PlanFixtures.FullCapability();
            profile.UnavailableLicenses.Add("DRILLING");   // ERROR 级
            setup.ProgramRoot.Children[0].Operations[0].TypeName = "SOME_FUTURE_OP";   // WARNING 级
            // INFO 级由 feature_type 云端待回填提示提供（每个含特征的导出固定一条）

            var plan = PlanFixtures.Export(setup, profile);
            var json = PlanSerializer.Serialize(plan);

            Assert.Contains(plan.Diagnostics, d => d.Level == "INFO");
            Assert.Contains(plan.Diagnostics, d => d.Level == "WARNING");
            Assert.Contains(plan.Diagnostics, d => d.Level == "ERROR");
            Assert.Empty(SchemaAsset.Validate(json));
        }
    }
}
