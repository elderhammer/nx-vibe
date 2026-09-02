using System;

namespace Autocam.PlanComparer.Core.Compare
{
    /// <summary>致命前置条件不满足（plan-comparer.md §3.10-1/2）：终止对比，不产出部分报告。</summary>
    public sealed class CompareAbortedException : Exception
    {
        public CompareAbortedException(string message) : base(message)
        {
        }
    }
}
