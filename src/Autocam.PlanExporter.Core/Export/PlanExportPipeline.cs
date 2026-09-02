using Autocam.Plan.Core.Dto;
using Autocam.Plan.Core.Plan;
using Autocam.Plan.Core.Diagnostics;

namespace Autocam.PlanExporter.Core.Export
{
    /// <summary>
    /// PlanExporter 门面（类名不叫 PlanExporter 以避开与命名空间 Autocam.PlanExporter 的
    /// C# 简单名冲突——任何嵌套在该命名空间下的消费方都会命中），§4.1 总流程：
    /// 1. 前置检查（PreconditionChecker）
    /// 2. 逐工序生效值拍平（ResolvedValueFlattener，含类型映射/许可/父组/几何 Tag 处置）
    /// 3. 锚点打包 + 碰撞检测（AnchorPackager）
    /// 4. 组装 + 引用闭合/双射校验（PlanAssembler）
    /// 5. 输出 PlanRoot（schema 校验与序列化由 PlanSerializer/PlanSchemaValidator 承担）
    /// 纯函数：输入快照 + 能力画像 → PlanRoot；同输入必同输出（§3.1e 确定性）。
    /// 会话只读（I1）由快照边界结构性保证，见 CamSetupSnapshot 注释。
    /// </summary>
    public static class PlanExportPipeline
    {
        public static PlanRoot Export(CamSetupSnapshot setup, CapabilityProfile profile)
        {
            // 1. 前置检查（致命条件抛 ExportAbortedException）
            PreconditionChecker.Check(setup);

            var diagnostics = new DiagnosticsCollector();

            // 2. 生效值拍平（§4.3；含类型映射 §4.4、许可/父组/能力探测处置 §3.2）
            var resolved = new ResolvedValueFlattener(profile, diagnostics).Flatten(setup);

            // 3. 锚点 + 对称碰撞检测（§4.5）
            var anchors = new AnchorPackager(diagnostics).Analyze(resolved);

            // 4. 组装 + 引用闭合/双射校验（§4.7）
            return new PlanAssembler(diagnostics).Assemble(setup, resolved, anchors);
        }
    }
}
