using System.Collections.Generic;

namespace Autocam.Plan.Core.Plan
{
    /// <summary>
    /// plan 输出对象图，字段与 schema/autocam-plan.schema.json 一一对应
    /// （C# 用 camelCase 属性，序列化时经 SnakeCaseNamingStrategy 落 snake_case）。
    /// 引用拓扑（plan-exporter.md §2.3）：workingstep → operation → tool；
    /// workingstep → feature → geometry_ref。
    /// </summary>
    public sealed class PlanRoot
    {
        public string PlanId { get; set; }
        public string InputRef { get; set; }
        public string Name { get; set; }
        public List<SetupEntry> Setups { get; set; } = new List<SetupEntry>();
        public ResourcesEntry Resources { get; set; } = new ResourcesEntry();
        public List<FeatureEntry> Features { get; set; } = new List<FeatureEntry>();
        public List<OperationEntry> Operations { get; set; } = new List<OperationEntry>();
        public List<WorkingstepEntry> Workingsteps { get; set; } = new List<WorkingstepEntry>();
        public WorkplanEntry Workplan { get; set; } = new WorkplanEntry();
        public List<DiagnosticEntry> Diagnostics { get; set; } = new List<DiagnosticEntry>();
    }

    public sealed class SetupEntry
    {
        public string SetupId { get; set; }
        public McsEntry Mcs { get; set; }
        public double? SafePlaneZ { get; set; }
        public int? FixtureOffset { get; set; }
    }

    public sealed class McsEntry
    {
        public double[] Origin { get; set; }
        public double[] ZAxis { get; set; }
        public double[] XAxis { get; set; }
    }

    public sealed class ResourcesEntry
    {
        public List<ToolEntry> Tools { get; set; } = new List<ToolEntry>();
    }

    public sealed class ToolEntry
    {
        public string ToolId { get; set; }

        /// <summary>NX 侧刀具组名（导出组名）。重建侧 find-or-create 复用同名模板组——
        /// 模板工件组（NONE/MILL_USER_DEFINED 等）参数不可读，按名复用保持两侧同构
        /// （schema 可选字段，plan-exporter.md 合同增强）。</summary>
        public string Name { get; set; }
        public string Type { get; set; }
        public double? Diameter { get; set; }
        public int? NumFlutes { get; set; }
        public double? FluteLength { get; set; }
        public double? LowerCornerRadius { get; set; }
    }

    public sealed class FeatureEntry
    {
        public string FeatureId { get; set; }

        /// <summary>AP224 特征分类。导出侧无法识别（NX 无 AP224 类型），落空串 + INFO 诊断，云端回填。</summary>
        public string FeatureType { get; set; }

        public GeometryRefEntry GeometryRef { get; set; }

        /// <summary>特征参数（直径/深度/螺距…）。导出侧不派生，省略（schema 可选）。</summary>
        public Dictionary<string, object> Params { get; set; }
    }

    public sealed class GeometryRefEntry
    {
        /// <summary>特征位置（孔心/质心）兜底锚点。face_ids/edge_ids 由云端按属性锚点匹配后回填，导出侧不产。</summary>
        public double[] AnchorPoint { get; set; }
    }

    public sealed class OperationEntry
    {
        public string OperationId { get; set; }
        public string OperationType { get; set; }
        public NxTemplateEntry NxTemplate { get; set; }
        public string ToolRef { get; set; }

        /// <summary>拍平后的策略生效值。SortedDictionary：键序确定 → 序列化字节级确定（§3.1e）。</summary>
        public SortedDictionary<string, object> Strategy { get; set; } = new SortedDictionary<string, object>(System.StringComparer.Ordinal);

        /// <summary>拍平后的技术参数生效值。同上，键序确定。</summary>
        public SortedDictionary<string, object> Technology { get; set; } = new SortedDictionary<string, object>(System.StringComparer.Ordinal);
    }

    public sealed class NxTemplateEntry
    {
        public string Type { get; set; }
        public string Subtype { get; set; }
    }

    public sealed class WorkingstepEntry
    {
        public string WorkingstepId { get; set; }
        public string FeatureRef { get; set; }
        public string OperationRef { get; set; }
        public string SetupRef { get; set; }
    }

    public sealed class WorkplanEntry
    {
        public WorkplanNodeEntry Root { get; set; }
        public List<WorkplanNodeEntry> Elements { get; set; } = new List<WorkplanNodeEntry>();
    }

    /// <summary>Program 组树前序投影节点：组节点带 name+children，工序叶子带 workingstep_ref。</summary>
    public sealed class WorkplanNodeEntry
    {
        public string Name { get; set; }
        public string WorkingstepRef { get; set; }
        public List<WorkplanNodeEntry> Children { get; set; } = new List<WorkplanNodeEntry>();
    }

    public sealed class DiagnosticEntry
    {
        /// <summary>"INFO" / "WARNING" / "ERROR"（枚举用串常量，避免序列化数字）。</summary>
        public string Level { get; set; }
        public string Code { get; set; }
        public string Detail { get; set; }
    }
}
