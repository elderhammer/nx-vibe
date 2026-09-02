using System.Linq;
using Autocam.Plan.Core.Dto;

namespace Autocam.PlanExporter.Core.Export
{
    /// <summary>
    /// §4.1 第 1 步：前置检查（plan-exporter.md §3.2）。
    /// 只处置"检测结果"——检测（读 NX 真实状态）属适配层；给定快照状态下做什么，在这里。
    /// 致命条件（无 CAMSetup / 无工序可导）抛 ExportAbortedException；
    /// 非致命条件（许可缺失、父组缺失、能力探测失败）在 Flatten 阶段按工序处置。
    /// </summary>
    public static class PreconditionChecker
    {
        public static void Check(CamSetupSnapshot setup)
        {
            if (setup == null)
            {
                throw new ExportAbortedException("前置条件 1 不满足：NX 会话/零件缺失（快照为 null），终止（plan-exporter.md §3.2-1）");
            }
            if (setup.ProgramRoot == null || CountOps(setup.ProgramRoot) == 0)
            {
                throw new ExportAbortedException("前置条件 2 不满足：CAMSetup 无工序可导，空 CAMSetup 无 ground truth，终止（plan-exporter.md §3.2-2）");
            }
        }

        private static int CountOps(GroupSnapshot group)
        {
            return group.Operations.Count + group.Children.Sum(CountOps);
        }
    }
}
