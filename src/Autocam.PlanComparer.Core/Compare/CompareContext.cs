using Autocam.Plan.Core.Dto;

namespace Autocam.PlanComparer.Core.Compare
{
    /// <summary>
    /// 对比上下文（决策点 D6）：RightCapability = 重建侧能力画像，已知跳过分类的结构化依据。
    /// right 侧缺失字段 ∈ UnsupportedParams → known_skip（info），否则 → extra 偏差。
    /// 缺省（null）→ 空画像：无跳过豁免，一切缺失按偏差计。
    /// </summary>
    public sealed class CompareContext
    {
        public CapabilityProfile RightCapability { get; set; }
    }
}
