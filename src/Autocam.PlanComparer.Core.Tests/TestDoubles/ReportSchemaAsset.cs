using System;
using System.IO;
using Autocam.Plan.Core.Serialization;
using NJsonSchema.Validation;
using System.Collections.Generic;

namespace Autocam.PlanComparer.Core.Tests.TestDoubles
{
    /// <summary>
    /// 报告 schema 作为测试资产加载（csproj 拷贝到输出目录）：
    /// schema 变更 → 契约测试直接变红，与实现双向锁定（plan-comparer.md §3.11-1）。
    /// 复用 Plan.Core 的 PlanSchemaValidator（其对 schema 是通用的）。
    /// </summary>
    public static class ReportSchemaAsset
    {
        private static readonly Lazy<PlanSchemaValidator> Lazy = new Lazy<PlanSchemaValidator>(() =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Schema", "autocam-compare-report.schema.json");
            return PlanSchemaValidator.LoadAsync(path).GetAwaiter().GetResult();
        });

        public static ICollection<ValidationError> Validate(string reportJson) => Lazy.Value.Validate(reportJson);
    }
}
