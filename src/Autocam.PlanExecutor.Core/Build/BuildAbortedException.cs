using System;

namespace Autocam.PlanExecutor.Core.Build
{
    /// <summary>
    /// 致命前置条件不满足（plan-executor.md §3.4-1/2/3：plan 为 null /
    /// 无工序可建 / workplan 根缺失）时抛出。非致命条件（引用悬空、类型不可映射、
    /// 能力探测失败）不抛异常，走诊断 + 逐条目跳过（镜像导出侧 I7）。
    /// </summary>
    public sealed class BuildAbortedException : Exception
    {
        public BuildAbortedException(string message) : base(message) { }
    }
}
