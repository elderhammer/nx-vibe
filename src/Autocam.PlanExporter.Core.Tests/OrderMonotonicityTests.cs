using System.Linq;
using Autocam.PlanExporter.Core.Dto;
using Autocam.PlanExporter.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanExporter.Core.Tests
{
    public class OrderMonotonicityTests
    {
        // 性质：§3.1c 顺序单调（保序嵌入）——workplan.elements 是 Program 树的前序投影。
        // 依据：plan-exporter.md §3.1c / §4.2
        // 失败含义：PlanComparer"结构对比退化为序列对齐"（nx-plugin-design.md §2.2）的前提不成立。
        [Fact]
        public void Workplan_elements_follow_preorder()
        {
            var plan = PlanFixtures.ExportDefault();

            var names = plan.Workplan.Elements.Select(e => e.Name).ToArray();
            Assert.Equal(new[] { "PROGRAM", "PROGRAM_1", "CAVITY_1", "DRILL_1" }, names);
        }

        // 性质：§3.1c——组树祖先-后代关系保持为 workplan 嵌套关系；工序叶子挂 workingstep_ref。
        // 依据：plan-exporter.md §3.1c / §4.2
        // 失败含义：重建侧无法还原 Program 组树层级与输出顺序。
        [Fact]
        public void Nesting_preserved()
        {
            var plan = PlanFixtures.ExportDefault();

            var root = plan.Workplan.Root;
            Assert.Equal("PROGRAM", root.Name);
            Assert.Single(root.Children);
            var pg1 = root.Children[0];
            Assert.Equal("PROGRAM_1", pg1.Name);
            Assert.Equal(new[] { "CAVITY_1", "DRILL_1" }, pg1.Children.Select(c => c.Name).ToArray());
            Assert.All(pg1.Children, c => Assert.False(string.IsNullOrEmpty(c.WorkingstepRef)));
        }

        // 性质：§3.1c——新工序插入 Program 树某位置 ⇒ workplan 在对应位置插入，其余相对顺序不变。
        // 依据：plan-exporter.md §3.1c
        // 失败含义：workplan 不随插入位置变化时，重建的输出顺序与 ground truth 不一致。
        [Fact]
        public void Inserted_operation_appears_at_corresponding_position()
        {
            var setup = PlanFixtures.DefaultSetup();
            var pg1 = setup.ProgramRoot.Children[0];
            var op3 = new OperationSnapshot
            {
                Name = "DRILL_2",
                TypeName = "DRILL",
                MethodGroup = setup.MethodGroups[1],
                ToolGroup = setup.ToolGroups[1],
                GeometryGroup = setup.GeometryGroups[0],
                ProgramGroup = pg1,
            };
            pg1.Operations.Insert(1, op3);   // CAVITY_1 之后、DRILL_1 之前

            var plan = PlanFixtures.Export(setup);

            var names = plan.Workplan.Elements.Select(e => e.Name).ToArray();
            Assert.Equal(new[] { "PROGRAM", "PROGRAM_1", "CAVITY_1", "DRILL_2", "DRILL_1" }, names);
            // 原有相对顺序保持
            Assert.True(System.Array.IndexOf(names, "CAVITY_1") < System.Array.IndexOf(names, "DRILL_1"));
        }

        // 性质：§4.2 setup 划分——同一 Program 组内工序共享同一 setup（几何组首次出现序）。
        // 依据：plan-exporter.md §4.2
        // 失败含义：工序挂错 setup，重建侧 MCS/装夹与 ground truth 错位。
        [Fact]
        public void Ops_in_same_program_group_share_setup()
        {
            var plan = PlanFixtures.ExportDefault();

            Assert.Single(plan.Setups);
            Assert.All(plan.Workingsteps, ws => Assert.Equal(plan.Setups[0].SetupId, ws.SetupRef));
        }
    }
}
