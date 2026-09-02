using Autocam.Plan.Core.Dto;
using Autocam.PlanExporter.Core.Export;
using Autocam.PlanExporter.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanExporter.Core.Tests
{
    public class PreconditionTests
    {
        // 性质：前置条件 1 处置（§3.2-1）——无会话/零件（快照为 null）→ 终止，不产出半成品。
        // 依据：plan-exporter.md §3.2-1（"报 error，终止"）
        // 失败含义：空会话被当成空工程导出，闭环会拿一份无意义的 plan 当 ground truth。
        [Fact]
        public void Null_snapshot_aborts()
        {
            Assert.Throws<ExportAbortedException>(
                () => PlanExportPipeline.Export(null, PlanFixtures.FullCapability()));
        }

        // 性质：前置条件 2 处置（§3.2-2）——CAMSetup 存在但无工序可导 → 终止，
        //       不产出空 plan 伪装成功。
        // 依据：plan-exporter.md §3.2-2（"空 CAMSetup 无 ground truth 可导"）
        // 失败含义：空 plan 进第②步重建会静默产出空工程，闭环失真。
        [Fact]
        public void Empty_cam_setup_aborts()
        {
            var setup = new CamSetupSnapshot
            {
                PartName = "empty.prt",
                ProgramRoot = PlanFixtures.NewGroup(GroupKind.Program, "PROGRAM", "MainProgram"),
            };

            Assert.Throws<ExportAbortedException>(
                () => PlanExportPipeline.Export(setup, PlanFixtures.FullCapability()));
        }
    }
}
