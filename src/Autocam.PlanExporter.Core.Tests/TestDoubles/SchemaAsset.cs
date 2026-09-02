using System;
using System.Collections.Generic;
using System.IO;
using Autocam.Plan.Core.Serialization;
using NJsonSchema.Validation;

namespace Autocam.PlanExporter.Core.Tests.TestDoubles
{
    /// <summary>
    /// 仓库 schema 作为测试资产加载（csproj 拷贝到输出目录）：
    /// schema 变更 → 契约测试直接变红，与实现双向锁定（后置条件 1 的执行者）。
    /// </summary>
    public static class SchemaAsset
    {
        private static readonly Lazy<PlanSchemaValidator> Lazy = new Lazy<PlanSchemaValidator>(() =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Schema", "autocam-plan.schema.json");
            return PlanSchemaValidator.LoadAsync(path).GetAwaiter().GetResult();
        });

        public static PlanSchemaValidator Validator => Lazy.Value;

        public static ICollection<ValidationError> Validate(string planJson) => Validator.Validate(planJson);
    }
}
