using System;
using System.Collections.Generic;

namespace Autocam.Plan.Core.Dto
{
    /// <summary>
    /// 当前 NX 会话的能力画像（适配层探测产物）：
    /// 前置条件 3（许可）与前置条件 5（版本能力探测）的处置依据。
    /// 测试伪造不同画像驱动分支（如 NX2312 有/无 bottom_clearance），不碰真实 NX 版本。
    /// </summary>
    public sealed class CapabilityProfile
    {
        /// <summary>
        /// 当前版本不可读的参数（plan 字段名），如 NX&lt;2312 时含 "bottom_clearance"。
        /// 处置：跳过该参数 + warning，绝不静默填充（§3.2-5）。
        /// </summary>
        public HashSet<string> UnsupportedParams { get; set; } = new HashSet<string>();

        /// <summary>
        /// 许可缺失的加工域（与 TypeMapper 的 domain 对齐，如 "TURNING"/"DRILLING"）。
        /// 处置：域内工序报 error 并跳过该工序，其余继续（§3.2-3 / I7）。
        /// </summary>
        public HashSet<string> UnavailableLicenses { get; set; } = new HashSet<string>();

        /// <summary>
        /// NX 侧写保护字段（plan 工序类型 → 字段名集，如 FACE_MILLING × floor_stock——
        /// NXOpen 层无条件回滚/不可表达，M3_Probe E 段实测）。与 UnsupportedParams 的区别：
        /// 版本缺能力（按参数名全局）vs 语义写保护（按工序类型×字段细粒度）。
        /// 处置：执行侧跳过写入 + 比较侧按同表 known_skip 豁免（绝不静默——豁免判定必须命中本表）。
        /// </summary>
        public Dictionary<string, HashSet<string>> UnwritableByPlanType { get; set; }
            = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
    }
}
