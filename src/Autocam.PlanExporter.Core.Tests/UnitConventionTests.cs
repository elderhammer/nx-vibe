using System;
using System.Collections.Generic;
using Autocam.PlanExporter.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanExporter.Core.Tests
{
    public class UnitConventionTests
    {
        // 性质：后置条件 8 + I6——全程 mm/rpm 口径：数值直通，不做任何单位换算。
        // 依据：plan-exporter.md §3.3-8 / §3.4 I6
        // 失败含义：任何换算（英寸/米、rps 等）都会让第②③步的参数对比失真。
        [Fact]
        public void Values_pass_through_unconverted()
        {
            var plan = PlanFixtures.ExportDefault();

            var cavity = PlanFixtures.OpByName(plan, "CAVITY_1");
            Assert.Equal(2.0, (double)cavity.Strategy["depth_per_cut"]);                 // mm
            Assert.Equal(6000.0, Convert.ToDouble(cavity.Technology["spindle_rpm"]));   // rpm
            Assert.Equal(0.3, (double)cavity.Strategy["floor_stock"]);                  // mm

            var tool = plan.Resources.Tools[0];
            Assert.Equal(10.0, (double)tool.Diameter);     // mm
            Assert.Equal(4, (int)tool.NumFlutes);

            var mcs = plan.Setups[0].Mcs;
            Assert.Equal(new[] { 0.0, 0.0, 0.0 }, mcs.Origin);
            Assert.Equal(new[] { 0.0, 0.0, 1.0 }, mcs.ZAxis);
            Assert.Equal(new[] { 1.0, 0.0, 0.0 }, mcs.XAxis);
            Assert.Equal(50.0, (double)plan.Setups[0].SafePlaneZ);
            Assert.Equal(1, (int)plan.Setups[0].FixtureOffset);
        }

        // 性质：后置条件 8——复合参数（stepover/feed_cut/depth）结构直通，键名与 schema 对齐。
        // 依据：plan-exporter.md §2.3 / nxopen-research.md §4.3/§4.4 示例
        // 失败含义：复合结构被拍散或键名漂移，schema 校验或重建侧解析失败。
        [Fact]
        public void Composite_values_pass_through()
        {
            var plan = PlanFixtures.ExportDefault();

            var stepover = (Dictionary<string, object>)PlanFixtures.OpByName(plan, "CAVITY_1").Strategy["stepover"];
            Assert.Equal("PERCENT", stepover["mode"]);
            Assert.Equal(50, stepover["value"]);

            var feedCut = (Dictionary<string, object>)PlanFixtures.OpByName(plan, "CAVITY_1").Technology["feed_cut"];
            Assert.Equal(1200.0, Convert.ToDouble(feedCut["value"]));
            Assert.Equal("MMPM", feedCut["unit"]);

            var depth = (Dictionary<string, object>)PlanFixtures.OpByName(plan, "DRILL_1").Strategy["depth"];
            Assert.Equal("THROUGH", depth["mode"]);
            Assert.Equal(25.0, Convert.ToDouble(depth["value"]));
        }
    }
}
