using System.Collections.Generic;
using System.Linq;
using Autocam.Plan.Core.Serialization;
using Autocam.PlanComparer.Core.Compare;
using Autocam.PlanComparer.Core.Tests.TestDoubles;
using Autocam.PlanExporter.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanComparer.Core.Tests
{
    public class ParamComparisonTests
    {
        // 性质：§4.5 技术参数维度——转速超相对 5% → deviation（r 计入 param_deviation_mean）。
        // 依据：plan-comparer.md §4.5 / §4.6
        // 失败含义：技术参数偏差漏报或不计入评分。
        [Fact]
        public void Technology_numeric_deviation_is_reported()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Operations[0].Technology["spindle_rpm"] = 1300;   // 100/1300 = 7.7% > 5%

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            var row = ComparerFixtures.Rows(report, "parameter", "deviation").Single();
            Assert.Equal("spindle_rpm", row.Field);
            Assert.Equal("OP-1", row.OperationRef);
            Assert.True(report.Scores.ParamDeviationMean > 0);
        }

        // 性质：§4.5 策略维度——枚举不一致 → deviation，两侧原值落行。
        // 依据：plan-comparer.md §4.5
        // 失败含义：策略枚举错（如顺逆铣）被吞 → 重建策略偏离意图。
        [Fact]
        public void Strategy_enum_mismatch_is_reported()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Operations[0].Strategy["cycle"] = "DRILL";

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            var row = ComparerFixtures.Rows(report, "strategy", "deviation").Single();
            Assert.Equal("cycle", row.Field);
            Assert.Equal("PECK", row.Left);
            Assert.Equal("DRILL", row.Right);
            Assert.Null(row.Delta);
        }

        // 性质：§4.5——复合对象递归到叶子路径（stepover.value），mode 一致时不误报。
        // 依据：plan-comparer.md §4.4（复合递归 + 点分路径）
        // 失败含义：复合参数整体序列化比较 → 键序/结构噪声误报，叶子差异漏报。
        [Fact]
        public void Composite_param_compares_leaf_paths()
        {
            var left = ComparerFixtures.OneOpPlan();
            left.Operations[0].Strategy["stepover"] = new Dictionary<string, object> { { "mode", "PERCENT" }, { "value", 50 } };
            var right = ComparerFixtures.OneOpPlan();
            right.Operations[0].Strategy["stepover"] = new Dictionary<string, object> { { "mode", "PERCENT" }, { "value", 60 } };

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            var row = ComparerFixtures.Rows(report, "strategy", "deviation").Single();
            Assert.Equal("stepover.value", row.Field);
            Assert.Equal(10, (int)row.Delta);
        }

        // 性质：§4.5——单侧缺字段：right 缺 → missing（left 有 right 无）；right 多出 → extra。
        // 依据：plan-comparer.md §4.5
        // 失败含义：字段集差异静默 → 继承缺省被误当一致。
        [Fact]
        public void Missing_and_extra_fields_are_reported()
        {
            var left = ComparerFixtures.OneOpPlan();
            var right = ComparerFixtures.OneOpPlan();
            right.Operations[0].Technology.Remove("spindle_rpm");   // right 缺
            right.Operations[0].Technology["coolant"] = "FLOOD";   // right 多出

            var report = PlanComparePipeline.Compare(left, right, ComparerFixtures.Context());

            var missing = ComparerFixtures.Rows(report, "parameter", "missing").Single();
            Assert.Equal("spindle_rpm", missing.Field);
            Assert.Equal(1200, (int)missing.Left);
            var extra = ComparerFixtures.Rows(report, "parameter", "extra").Single();
            Assert.Equal("coolant", extra.Field);
        }

        // 性质：§4.4 归一——反序列化 plan 的值是 JValue/JObject（真实闭环输入形态），
        //       与手写 CLR 值的同一 plan 对比必须零偏差。
        // 依据：plan-comparer.md §4.4
        // 失败含义：JToken 与 CLR 值比较不等 → 真实闭环里全量误报。
        [Fact]
        public void Deserialized_plan_compares_identical_to_clr_plan()
        {
            var clr = ComparerFixtures.OneOpPlan();
            var json = PlanSerializer.Serialize(clr);
            var deserialized = PlanDeserializer.Deserialize(json, SchemaAsset.Validator);

            var report = PlanComparePipeline.Compare(clr, deserialized, ComparerFixtures.Context());

            Assert.Empty(report.Deviations);
        }
    }
}
