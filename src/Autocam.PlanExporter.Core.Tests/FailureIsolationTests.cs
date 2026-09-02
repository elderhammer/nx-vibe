using System.Linq;
using Autocam.Plan.Core.Dto;
using Autocam.PlanExporter.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanExporter.Core.Tests
{
    public class FailureIsolationTests
    {
        // 性质：§4.4——typeName 未映射时 operation_type="other" + warning，
        //       原始串全量保留进 nx_template（不猜测、不丢弃），工序不跳过。
        // 依据：plan-exporter.md §4.4 / nx-plugin-design.md §6 近似工序口径
        // 失败含义：近似工序在重建侧失去"按真实类型落地"的依据。
        [Fact]
        public void Unknown_typename_maps_to_other_and_preserves_template()
        {
            var setup = PlanFixtures.DefaultSetup();
            var drillOp = setup.ProgramRoot.Children[0].Operations[1];
            drillOp.TypeName = "SOME_FUTURE_OP";
            drillOp.SubtypeName = "MILL_OPEN";

            var plan = PlanFixtures.Export(setup);

            Assert.Equal(2, plan.Operations.Count);   // 未跳过
            var drill = PlanFixtures.OpByName(plan, "DRILL_1");
            Assert.Equal("other", drill.OperationType);
            Assert.Equal("SOME_FUTURE_OP", drill.NxTemplate.Type);
            Assert.Equal("MILL_OPEN", drill.NxTemplate.Subtype);
            Assert.Contains(plan.Diagnostics, d => d.Level == "WARNING" && d.Code == "TYPE_UNMAPPED");
        }

        // 性质：前置条件 3 + I7——许可缺失的域内工序报 error 跳过，其余工序不受任何影响。
        // 依据：plan-exporter.md §3.2-3 / §3.4 I7
        // 失败含义：单域许可缺失拖垮整份导出，或静默产出一份假装成功的残缺 plan。
        [Fact]
        public void License_missing_skips_only_that_domain()
        {
            var setup = PlanFixtures.DefaultSetup();
            var profile = PlanFixtures.FullCapability();
            profile.UnavailableLicenses.Add("DRILLING");

            var plan = PlanFixtures.Export(setup, profile);

            Assert.Single(plan.Operations);   // 只剩 CAVITY_1
            var cavity = PlanFixtures.OpByName(plan, "CAVITY_1");
            Assert.Equal(0.3, (double)cavity.Strategy["floor_stock"]);   // 幸存工序条目完好
            Assert.Contains(plan.Diagnostics, d =>
                d.Level == "ERROR" && d.Code == "LICENSE_MISSING" && d.Detail.Contains("DRILL_1"));
            Assert.DoesNotContain(plan.Workplan.Elements, e => e.Name == "DRILL_1");
        }

        // 性质：前置条件 5——能力探测失败的参数跳过 + warning，该工序其余参数不受影响。
        // 依据：plan-exporter.md §3.2-5（如 bottom_clearance 需 NX2312+）
        // 失败含义：把读不到的版本新参数静默填充或拖垮整道工序。
        [Fact]
        public void Unsupported_param_skipped_with_warning()
        {
            var setup = PlanFixtures.DefaultSetup();
            var drillOp = setup.ProgramRoot.Children[0].Operations[1];
            drillOp.Params["bottom_clearance"] = new OpParam { IsSet = true, Value = 2.0 };
            var profile = PlanFixtures.FullCapability();
            profile.UnsupportedParams.Add("bottom_clearance");

            var plan = PlanFixtures.Export(setup, profile);

            var drill = PlanFixtures.OpByName(plan, "DRILL_1");
            Assert.False(drill.Strategy.ContainsKey("bottom_clearance"));   // 跳过该参数
            Assert.Equal("PECK", drill.Strategy["cycle"]);                   // 其余参数完好
            Assert.Contains(plan.Diagnostics, d =>
                d.Level == "WARNING" && d.Code == "CAPABILITY_UNSUPPORTED" && d.Detail.Contains("bottom_clearance"));
        }

        // 性质：前置条件 6 + I7——父组缺失的工序报 error 跳过，其余照常，剩余条目双射保持。
        // 依据：plan-exporter.md §3.2-6 / §3.4 I7 / §3.3-5
        // 失败含义：坏工序连带破坏其余条目，或产出缺引用的 plan。
        [Fact]
        public void Missing_geometry_group_skips_only_that_op()
        {
            var setup = PlanFixtures.DefaultSetup();
            var drillOp = setup.ProgramRoot.Children[0].Operations[1];
            drillOp.GeometryGroup = null;

            var plan = PlanFixtures.Export(setup);

            Assert.Single(plan.Operations);
            Assert.Single(plan.Workingsteps);
            Assert.Single(plan.Features);
            Assert.Contains(plan.Diagnostics, d =>
                d.Level == "ERROR" && d.Code == "MISSING_PARENT_GROUP" && d.Detail.Contains("DRILL_1"));
            Assert.Equal(0.3, (double)PlanFixtures.OpByName(plan, "CAVITY_1").Strategy["floor_stock"]);
        }
    }
}
