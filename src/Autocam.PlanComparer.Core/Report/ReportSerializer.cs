using Autocam.Plan.Core.Serialization;
using Newtonsoft.Json;

namespace Autocam.PlanComparer.Core.Report
{
    /// <summary>
    /// 报告对象图 → JSON。与 PlanSerializer 共用同一命名策略（字母序 + snake_case +
    /// null 省略），保证 §3.6 字节级确定、报告与 plan 的序列化口径一致。
    /// </summary>
    public static class ReportSerializer
    {
        public static string Serialize(ComparisonReport report)
        {
            return JsonConvert.SerializeObject(report, PlanSerializer.CreateSettings());
        }
    }
}
