using System.Collections.Generic;
using Autocam.Plan.Core.Plan;

namespace Autocam.PlanComparer.Core.Report
{
    /// <summary>
    /// 对比报告对象图，字段与 schema/autocam-compare-report.schema.json 一一对应
    /// （C# camelCase 属性经 OrderedSnakeCaseContractResolver 落 snake_case；
    /// null 省略、属性字母序，字节级确定——plan-comparer.md §3.6）。
    /// 只含非一致偏差行；汇总计数与评分定义见 plan-comparer.md §2.3/§4.6。
    /// </summary>
    public sealed class ComparisonReport
    {
        /// <summary>确定性生成："cmp-{left.plan_id}-{right.plan_id}"（§3.6）。</summary>
        public string ReportId { get; set; }

        public ReportSide Left { get; set; }
        public ReportSide Right { get; set; }

        /// <summary>偏差行（只含非一致项，行序规则 §3.11-3）。</summary>
        public List<DeviationEntry> Deviations { get; set; } = new List<DeviationEntry>();

        public SummaryEntry Summary { get; set; } = new SummaryEntry();
        public ScoresEntry Scores { get; set; } = new ScoresEntry();

        /// <summary>刀路维度预留通道（决策点 D5）：MVP 恒 null，序列化省略。</summary>
        public object Toolpath { get; set; }

        /// <summary>比较过程自身诊断（输入两侧 plan.diagnostics 留在各自 plan 内，不复制）。</summary>
        public List<DiagnosticEntry> Diagnostics { get; set; } = new List<DiagnosticEntry>();
    }

    /// <summary>七维度常量（plan-comparer.md §4.5；toolpath 预留，other 兜底——反面清单 #4）。</summary>
    public static class ReportDimensions
    {
        public const string Structure = "structure";
        public const string Tool = "tool";
        public const string Parameter = "parameter";
        public const string Strategy = "strategy";
        public const string Mcs = "mcs";
        public const string Geometry = "geometry";
        public const string Toolpath = "toolpath";
        public const string Other = "other";
    }

    /// <summary>偏差行 kind 常量（plan-comparer.md §2.3；other 兜底）。</summary>
    public static class DeviationKinds
    {
        public const string Deviation = "deviation";
        public const string Missing = "missing";
        public const string Extra = "extra";
        public const string TypeMismatch = "type_mismatch";
        public const string OrderSwap = "order_swap";
        public const string KnownSkip = "known_skip";
        public const string Unaligned = "unaligned";
        public const string Other = "other";
    }

    /// <summary>对比一侧的来源标识（schema #/definitions/side）。</summary>
    public sealed class ReportSide
    {
        public string PlanId { get; set; }
        public string Name { get; set; }
        public string InputRef { get; set; }
    }

    /// <summary>单条偏差行（schema #/definitions/deviation；operation_ref 空 = 组级/setup 级行）。</summary>
    public sealed class DeviationEntry
    {
        public string Dimension { get; set; }
        public string OperationRef { get; set; }
        public string Field { get; set; }
        public string Kind { get; set; }
        public string Severity { get; set; }
        public object Left { get; set; }
        public object Right { get; set; }
        public double? Delta { get; set; }
        public double? Tolerance { get; set; }
        public string Detail { get; set; }
    }

    /// <summary>分维度汇总计数（schema #/definitions/summary，与偏差行一一可核对）。</summary>
    public sealed class SummaryEntry
    {
        public StructureSummaryEntry Structure { get; set; } = new StructureSummaryEntry();
        public DimensionSummaryEntry Tool { get; set; } = new DimensionSummaryEntry();
        public ParamSummaryEntry Parameter { get; set; } = new ParamSummaryEntry();
        public ParamSummaryEntry Strategy { get; set; } = new ParamSummaryEntry();
        public DimensionSummaryEntry Mcs { get; set; } = new DimensionSummaryEntry();
        public GeometrySummaryEntry Geometry { get; set; } = new GeometrySummaryEntry();
    }

    public sealed class StructureSummaryEntry
    {
        public int MatchedOps { get; set; }
        public int TotalOps { get; set; }
        public int Missing { get; set; }
        public int Extra { get; set; }
        public int TypeMismatches { get; set; }
        public int OrderSwaps { get; set; }
        public int GroupDiffs { get; set; }
    }

    public class DimensionSummaryEntry
    {
        public int Compared { get; set; }
        public int Matched { get; set; }
        public int Missing { get; set; }
        public int Extra { get; set; }
        public int Deviations { get; set; }
    }

    public sealed class ParamSummaryEntry : DimensionSummaryEntry
    {
        public int KnownSkips { get; set; }
    }

    public sealed class GeometrySummaryEntry : DimensionSummaryEntry
    {
        public int Collisions { get; set; }
    }

    /// <summary>汇总评分（schema #/definitions/scores；公式 §4.6，分母取 max 保证对称性 §3.2）。</summary>
    public sealed class ScoresEntry
    {
        public double StructureConsistency { get; set; }
        public double ParamDeviationMean { get; set; }
        public double GeometryMatchRate { get; set; }
    }
}
