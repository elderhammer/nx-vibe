using System;

namespace Autocam.PlanExporter.Core.Serialization
{
    /// <summary>
    /// plan.json 未通过 schema v3 校验（PlanDeserializer 整体拒绝的异常，
    /// plan-executor.md 前置条件：schema 非法 → 整体拒绝，不逐条降级）。
    /// </summary>
    public sealed class PlanValidationException : Exception
    {
        public PlanValidationException(string message) : base(message) { }
    }
}
