using System.Collections.Generic;

namespace Autocam.PlanExporter.Core.Dto
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
    }
}
