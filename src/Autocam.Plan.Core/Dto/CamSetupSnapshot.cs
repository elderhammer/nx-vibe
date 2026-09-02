using System.Collections.Generic;

namespace Autocam.Plan.Core.Dto
{
    /// <summary>
    /// 操作级参数的读取结果：显式设置，或继承态（未在操作上设置）。
    /// 对应 plan-exporter.md §2.1 关键口径：Builder 参数未显式设置时继承父组/方法组默认值。
    /// </summary>
    public sealed class OpParam
    {
        /// <summary>true = 操作级显式设置；false = 继承态，需沿 方法组→几何组→刀具组→模板根 向上解析（§4.3）。</summary>
        public bool IsSet { get; set; }

        /// <summary>生效值（plan 口径：数值 mm/rpm、枚举串、复合对象如 stepover{PERCENT,value}）。</summary>
        public object Value { get; set; }
    }

    public enum GroupKind
    {
        Program,
        Method,
        Tool,
        Geometry,
    }

    public sealed class GroupSnapshot
    {
        public GroupKind Kind { get; set; }

        /// <summary>NX 组名，如 PROGRAM_1 / MILL_ROUGH / T1_D10 / MCS_1（setup 划分与 workplan 投影用组名）。</summary>
        public string Name { get; set; }

        public string DisplayName { get; set; }

        /// <summary>组级 Builder 回读参数（组参数天然为"已设置"）。key = plan 字段名，与 ParamRegistry 对齐。</summary>
        public Dictionary<string, object> Params { get; set; } = new Dictionary<string, object>();

        /// <summary>Program 视图下的子组。前序遍历次序即刀路输出次序（§3.1c / I4 的输入保证）。</summary>
        public List<GroupSnapshot> Children { get; set; } = new List<GroupSnapshot>();

        /// <summary>挂在本组下的工序，列表顺序即组内输出顺序。仅 Program 组承载。</summary>
        public List<OperationSnapshot> Operations { get; set; } = new List<OperationSnapshot>();
    }

    public sealed class OperationSnapshot
    {
        public string Name { get; set; }

        /// <summary>NX 操作类型名（typeName），如 CAVITY_MILL / DRILL。TypeMapper 的输入。</summary>
        public string TypeName { get; set; }

        public string SubtypeName { get; set; } = "";

        public GroupSnapshot ProgramGroup { get; set; }
        public GroupSnapshot MethodGroup { get; set; }
        public GroupSnapshot ToolGroup { get; set; }
        public GroupSnapshot GeometryGroup { get; set; }

        /// <summary>操作级参数：IsSet=false 为继承态。key = plan 字段名。</summary>
        public Dictionary<string, OpParam> Params { get; set; } = new Dictionary<string, OpParam>();

        /// <summary>关联几何 Tag（Face/Edge，适配层填充，顺序须确定以保证锚点选择确定）。Core 只做不透明匹配。</summary>
        public List<object> GeometryTags { get; set; } = new List<object>();
    }

    public sealed class FaceSnapshot
    {
        public object Tag { get; set; }

        /// <summary>质心 [x,y,z]，mm，模型局部坐标（UF_MODL_ask_face_data 精确值）。</summary>
        public double[] Centroid { get; set; }

        /// <summary>面积 mm²（UF_MODL_ask_face_area 精确值）。</summary>
        public double Area { get; set; }

        /// <summary>11 种标准面类型之一（UF_MODL_ask_face_data）。</summary>
        public string FaceType { get; set; }

        /// <summary>单位法向（AskFaceNormals）。</summary>
        public double[] Normal { get; set; }
    }

    public sealed class EdgeSnapshot
    {
        public object Tag { get; set; }
        public double Length { get; set; }
        public string Convexity { get; set; }
        public double[] EndpointA { get; set; }
        public double[] EndpointB { get; set; }
    }

    /// <summary>
    /// NX 工程对象图的纯数据快照——Core 的唯一输入（NX 适配层构建）。
    /// 快照边界同时是 I1（只读）的结构性保证：Core 见不到任何 NX 对象，
    /// 编译期杜绝写会话；会话安全性测试随适配层在 NX 侧做集成验证（nx-plugin-design.md §4）。
    /// </summary>
    public sealed class CamSetupSnapshot
    {
        public string PartName { get; set; }
        public string InputRef { get; set; }

        /// <summary>Program 视图根组（组树 + 挂载工序，前序 = 刀路输出顺序）。</summary>
        public GroupSnapshot ProgramRoot { get; set; }

        public List<GroupSnapshot> MethodGroups { get; set; } = new List<GroupSnapshot>();
        public List<GroupSnapshot> ToolGroups { get; set; } = new List<GroupSnapshot>();
        public List<GroupSnapshot> GeometryGroups { get; set; } = new List<GroupSnapshot>();

        /// <summary>模板根默认值：继承链的最终上游（key = plan 字段名）。</summary>
        public Dictionary<string, object> TemplateDefaults { get; set; } = new Dictionary<string, object>();

        public List<FaceSnapshot> Faces { get; set; } = new List<FaceSnapshot>();
        public List<EdgeSnapshot> Edges { get; set; } = new List<EdgeSnapshot>();
    }
}
