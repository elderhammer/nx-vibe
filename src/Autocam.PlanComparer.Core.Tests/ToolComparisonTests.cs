using System.Linq;
using Autocam.Plan.Core.Plan;
using Autocam.PlanComparer.Core.Compare;
using Autocam.PlanComparer.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanComparer.Core.Tests
{
    public class ToolComparisonTests
    {
        // 性质：§4.5 刀具维度——直径按 AbsoluteMm 0.01 判定，偏差行带 delta/tolerance。
        // 依据：plan-comparer.md §4.5 / §3.4
        // 失败含义：刀具直径偏差漏报 → 重建用了错误的刀。
        [Fact]
        public void Diameter_deviation_is_reported()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Resources.Tools[0].Diameter = 7.0;

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            var row = ComparerFixtures.Rows(report, "tool", "deviation").Single();
            Assert.Equal("diameter", row.Field);
            Assert.Equal("OP-1", row.OperationRef);
            Assert.Equal(0.2, (double)row.Delta, 5);
            Assert.Equal(0.01, (double)row.Tolerance);
        }

        // 性质：§4.5——刀具类型枚举严格相等（类型错 = 换刀，不容差）。
        // 依据：plan-comparer.md §4.5
        // 失败含义：刀具类型不一致被吞 → 钻头重建成了铣刀。
        [Fact]
        public void Tool_type_mismatch_is_reported()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Resources.Tools[0].Type = "END_MILL";

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            var row = ComparerFixtures.Rows(report, "tool", "deviation").Single();
            Assert.Equal("type", row.Field);
            Assert.Equal("DRILL", row.Left);
            Assert.Equal("END_MILL", row.Right);
        }

        // 性质：§4.5——刃数为整数精确口径，差 1 即偏差。
        // 依据：plan-comparer.md §3.4（整数 → Exact）
        // 失败含义：刃数差异被数值容差吞掉。
        [Fact]
        public void Num_flutes_differs_by_one_is_a_deviation()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Resources.Tools[0].NumFlutes = 3;

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            Assert.Single(ComparerFixtures.Rows(report, "tool", "deviation"));
        }

        // 性质：§4.5——刀具表计数不同 → 计数行（未引用刀具的增删也要可见）。
        // 依据：plan-comparer.md §4.5
        // 失败含义：多余的刀具组静默 → 资源表差异不可见。
        [Fact]
        public void Extra_tool_in_right_resources_is_reported()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Resources.Tools.Add(new ToolEntry { ToolId = "T-9", Type = "DRILL", Diameter = 3.0 });

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            var row = ComparerFixtures.Rows(report, "tool", "extra").Single();
            Assert.Null(row.OperationRef);
        }
    }
}
