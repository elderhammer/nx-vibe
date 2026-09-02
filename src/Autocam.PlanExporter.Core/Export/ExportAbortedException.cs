using System;

namespace Autocam.PlanExporter.Core.Export
{
    /// <summary>
    /// 致命前置条件不满足（§3.2 中"报 error，终止"类）时抛出：
    /// 无 CAMSetup / 无工序可导。非致命条件（许可缺失、父组缺失、能力探测失败）
    /// 不抛异常，走诊断 + 跳过，保证 I7（失败不破坏）。
    /// </summary>
    public sealed class ExportAbortedException : Exception
    {
        public ExportAbortedException(string message) : base(message) { }
    }
}
