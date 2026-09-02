using Autocam.Plan.Core.Dto;
using Autocam.PlanExporter.Core.Tests.TestDoubles;
using Newtonsoft.Json;
using Xunit;

namespace Autocam.PlanExporter.Core.Tests
{
    public class ResolvedValueTests
    {
        // 性质：§4.3 生效值拍平——操作级显式值优先于组值（导出生效值而非仅显式值）。
        // 依据：plan-exporter.md §2.1 警告框 / §4.3
        // 失败含义：只导显式值时，重建侧参数与 ground truth 必然不一致。
        [Fact]
        public void Explicit_op_value_wins_over_method_group()
        {
            var plan = PlanFixtures.ExportDefault();
            var cavity = PlanFixtures.OpByName(plan, "CAVITY_1");

            // 方法组 floor_stock=0.5，操作级显式 0.3 → 拍平为 0.3
            Assert.Equal(0.3, (double)cavity.Strategy["floor_stock"]);
        }

        // 性质：§4.3——继承态沿方法组解析（默认继承链第一级：方法组 → 几何组 → 刀具组 → 模板根）。
        // 依据：plan-exporter.md §4.3
        // 失败含义：继承链解析错误会拍平出错误生效值。
        [Fact]
        public void Inherited_param_resolves_from_method_group()
        {
            var plan = PlanFixtures.ExportDefault();
            var cavity = PlanFixtures.OpByName(plan, "CAVITY_1");

            Assert.Equal(2.0, (double)cavity.Strategy["depth_per_cut"]);
        }

        // 性质：§4.3 + ParamRegistry——Tool 优先源参数（cross_over_distance）从刀具组解析。
        // 依据：plan-exporter.md §4.3 / nxopen-research.md §4.3 crossOverDistance 为 InheritableToolDepBuilder
        // 失败含义：优先源表失效时，刀具相关参数会从错误的组取到错误值。
        [Fact]
        public void Tool_owned_param_resolves_from_tool_group()
        {
            var plan = PlanFixtures.ExportDefault();
            var cavity = PlanFixtures.OpByName(plan, "CAVITY_1");

            Assert.Equal(5.0, (double)cavity.Strategy["cross_over_distance"]);
        }

        // 性质：§4.3——模板根是继承链的最终上游。
        // 依据：plan-exporter.md §4.3（继承链深度 ≤ 3：操作 → 组 → 模板根）
        // 失败含义：模板根默认值丢失，重建侧会取到 NX 自身模板值而非 ground truth 值。
        [Fact]
        public void Template_default_is_final_fallback()
        {
            var setup = PlanFixtures.DefaultSetup();
            setup.TemplateDefaults["retract_speed"] = 3000;

            var plan = PlanFixtures.Export(setup);
            var cavity = PlanFixtures.OpByName(plan, "CAVITY_1");

            Assert.Equal(3000.0, System.Convert.ToDouble(cavity.Technology["retract_speed"]));
        }

        // 性质：§3.1d 信息量单调——不可解析参数不伪造值：字段缺省，但诊断必增（绝不静默省略）。
        // 依据：plan-exporter.md §3.1d / §3.3-2
        // 失败含义：伪造默认值会污染对比基线；静默省略则丢失偏差可观测性。
        [Fact]
        public void Unresolvable_params_are_omitted_with_warning()
        {
            var plan = PlanFixtures.ExportDefault();
            var cavity = PlanFixtures.OpByName(plan, "CAVITY_1");

            Assert.False(cavity.Strategy.ContainsKey("cut_order"));
            Assert.Contains(plan.Diagnostics, d =>
                d.Level == "WARNING" && d.Code == "UNRESOLVED_PARAMS" && d.Detail.Contains("cut_order"));
        }

        // 性质：§3.1b 对象级单调扩展——新增工序（不修改已有对象）不改变已有条目的任何字段。
        // 依据：plan-exporter.md §3.1b
        // 失败含义：条目间存在隐藏耦合（共享可变缓存/全局状态），PlanComparer 逐工序独立对比的前提被破坏。
        [Fact]
        public void Adding_operation_does_not_alter_existing_entries()
        {
            var plan1 = PlanFixtures.ExportDefault();

            var setup2 = PlanFixtures.DefaultSetup();
            var pg1 = setup2.ProgramRoot.Children[0];
            var op3 = new OperationSnapshot
            {
                Name = "DRILL_2",
                TypeName = "DRILL",
                MethodGroup = setup2.MethodGroups[1],
                ToolGroup = setup2.ToolGroups[1],
                GeometryGroup = setup2.GeometryGroups[0],
                ProgramGroup = pg1,
            };
            pg1.Operations.Add(op3);
            var plan2 = PlanFixtures.Export(setup2);

            // 已有条目逐字段相等（operation/feature/workingstep 三类条目）
            Assert.Equal(JsonConvert.SerializeObject(plan1.Operations[0]), JsonConvert.SerializeObject(plan2.Operations[0]));
            Assert.Equal(JsonConvert.SerializeObject(plan1.Features[0]), JsonConvert.SerializeObject(plan2.Features[0]));
            Assert.Equal(JsonConvert.SerializeObject(plan1.Workingsteps[0]), JsonConvert.SerializeObject(plan2.Workingsteps[0]));
        }

        // 性质：§3.1b 反例（文档化拍平语义，非恒等断言）——修改继承来源会改变依赖它的条目，
        //       因为导出固化的是"导出时刻"的生效值。
        // 依据：plan-exporter.md §3.1b 反例
        // 失败含义：若此测试意外恒等（修改继承来源后导出不变），说明拍平失效——导出的是引用而非值。
        [Fact]
        public void Changing_inheritance_source_changes_dependent_entries()
        {
            var plan1 = PlanFixtures.ExportDefault();
            Assert.Equal(2.0, (double)PlanFixtures.OpByName(plan1, "CAVITY_1").Strategy["depth_per_cut"]);

            var setup2 = PlanFixtures.DefaultSetup();
            setup2.MethodGroups[0].Params["depth_per_cut"] = 3.0;   // MILL_ROUGH 组默认值修改
            var plan2 = PlanFixtures.Export(setup2);

            Assert.Equal(3.0, (double)PlanFixtures.OpByName(plan2, "CAVITY_1").Strategy["depth_per_cut"]);
        }
    }
}
