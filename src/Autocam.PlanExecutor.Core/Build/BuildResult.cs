using System.Collections.Generic;
using Autocam.Plan.Core.Dto;
using Autocam.Plan.Core.Plan;

namespace Autocam.PlanExecutor.Core.Build
{
    /// <summary>
    /// Build 双产物（plan-executor.md §2.2/§2.3）：
    /// Commands → 适配层执行（NX 建工程 prj′）；
    /// Simulated → 重建结果的纯数据投影，供 round-trip 测试复用 PlanExporter。
    /// </summary>
    public sealed class BuildResult
    {
        public List<RebuildCommand> Commands { get; } = new List<RebuildCommand>();
        public CamSetupSnapshot Simulated { get; set; }
        public List<DiagnosticEntry> Diagnostics { get; } = new List<DiagnosticEntry>();
    }
}
