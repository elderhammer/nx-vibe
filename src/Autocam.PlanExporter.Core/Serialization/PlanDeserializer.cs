using System.Linq;
using Autocam.PlanExporter.Core.Plan;
using Newtonsoft.Json;

namespace Autocam.PlanExporter.Core.Serialization
{
    /// <summary>
    /// PlanParser（轻量版）：plan.json → PlanRoot。
    /// 职责：schema 校验（不过 → PlanValidationException 整体拒绝）+ 反序列化
    /// （与 PlanSerializer 同一命名策略，保证往返一致）。
    /// 强类型模型已由 PlanRoot 承担，本类就是"解析"的全部——不做业务校验
    /// （引用闭合等属 PlanExecutor 前置检查）。
    /// </summary>
    public static class PlanDeserializer
    {
        public static PlanRoot Deserialize(string planJson, PlanSchemaValidator validator)
        {
            var errors = validator.Validate(planJson);
            if (errors.Count > 0)
            {
                var summary = string.Join("; ", errors.Take(5).Select(e => string.Format("{0}: {1}", e.Path, e.Kind)));
                throw new PlanValidationException(
                    string.Format("plan.json 未通过 schema v3 校验（{0} 处，前 5：{1}）", errors.Count, summary));
            }
            return JsonConvert.DeserializeObject<PlanRoot>(planJson, PlanSerializer.CreateSettings());
        }
    }
}
