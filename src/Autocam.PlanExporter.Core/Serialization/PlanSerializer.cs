using System.Collections.Generic;
using System.Linq;
using Autocam.PlanExporter.Core.Plan;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Autocam.PlanExporter.Core.Serialization
{
    /// <summary>
    /// plan 对象图 → JSON。
    /// 确定性（§3.1e）要求字节级可复现，这里做到两点：
    /// 1) 属性按名（snake_case 后）字母序输出——不依赖反射返回顺序；
    /// 2) null 不输出（schema draft-07 的 "type":"string" 不接受 null）。
    /// 字典键序由调用侧保证（strategy/technology 用 SortedDictionary，见 PlanModel）。
    /// </summary>
    public static class PlanSerializer
    {
        public static string Serialize(PlanRoot plan)
        {
            return JsonConvert.SerializeObject(plan, CreateSettings());
        }

        /// <summary>序列化/反序列化共用设置（PlanDeserializer 与 Serialize 必须使用同一命名策略才能互逆）。</summary>
        public static JsonSerializerSettings CreateSettings()
        {
            return new JsonSerializerSettings
            {
                ContractResolver = new OrderedSnakeCaseContractResolver(),
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.Indented,
            };
        }

        private sealed class OrderedSnakeCaseContractResolver : DefaultContractResolver
        {
            public OrderedSnakeCaseContractResolver()
            {
                NamingStrategy = new SnakeCaseNamingStrategy();
            }

            protected override IList<JsonProperty> CreateProperties(System.Type type, MemberSerialization memberSerialization)
            {
                return base.CreateProperties(type, memberSerialization)
                    .OrderBy(p => p.PropertyName, System.StringComparer.Ordinal)
                    .ToList();
            }
        }
    }
}
