using System.Collections.Generic;
using System.Threading.Tasks;
using NJsonSchema;
using NJsonSchema.Validation;

namespace Autocam.PlanExporter.Core.Serialization
{
    /// <summary>
    /// 后置条件 1 的执行者：plan.json 必须通过 autocam-plan.schema.json v3 校验。
    /// schema 对象由调用侧加载注入（生产：部署目录；测试：仓库 schema 资产），
    /// 避免 Core 依赖文件路径。
    /// </summary>
    public sealed class PlanSchemaValidator
    {
        private readonly JsonSchema _schema;

        public PlanSchemaValidator(JsonSchema schema)
        {
            _schema = schema;
        }

        public static async Task<PlanSchemaValidator> LoadAsync(string schemaPath)
        {
            var schema = await JsonSchema.FromFileAsync(schemaPath).ConfigureAwait(false);
            return new PlanSchemaValidator(schema);
        }

        public ICollection<ValidationError> Validate(string planJson)
        {
            return _schema.Validate(planJson);
        }
    }
}
