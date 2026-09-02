using System.Collections.Generic;
using System.Linq;
using Autocam.Plan.Core.Dto;
using Autocam.Plan.Core.Policies;
using Autocam.Plan.Core.Diagnostics;

namespace Autocam.PlanExporter.Core.Export
{
    /// <summary>
    /// 单个工序的拍平结果：生效值（resolved）而非仅显式值。
    /// 这是 §3.1b 单调性（条目间相互独立）的机制来源——继承来源一旦拍平固化，
    /// 后续新增对象不会污染已有条目。
    /// </summary>
    public sealed class OpResolved
    {
        public OperationSnapshot Op { get; set; }

        /// <summary>TypeMapper 映射结果（operation_type 枚举值）。</summary>
        public string OperationType { get; set; }

        /// <summary>加工域（许可检查用，与 CapabilityProfile.UnavailableLicenses 对齐）。</summary>
        public string Domain { get; set; }

        public SortedDictionary<string, object> Strategy { get; set; } =
            new SortedDictionary<string, object>(System.StringComparer.Ordinal);

        public SortedDictionary<string, object> Technology { get; set; } =
            new SortedDictionary<string, object>(System.StringComparer.Ordinal);

        public List<FaceSnapshot> Faces { get; set; } = new List<FaceSnapshot>();
        public List<EdgeSnapshot> Edges { get; set; } = new List<EdgeSnapshot>();
    }

    /// <summary>
    /// §4.1 第 2 步 / §4.3：逐 Operation 回读 + 生效值拍平（继承解析，核心算法）。
    /// 规则：操作级显式值 &gt; 继承链（方法组 → 几何组 → 刀具组 → 模板根，
    /// ParamRegistry.Source 可调优先源）；不可解析 → warning + 省字段
    /// （§3.1d 只增诊断不减字段：字段可缺，诊断必增，绝不伪造值）。
    /// 处置前置条件 3/5/6 的非致命分支：许可缺失 → error 跳过工序；
    /// 父组缺失/未登记 → error 跳过工序；能力探测失败 → warning 跳过该参数。
    /// </summary>
    public sealed class ResolvedValueFlattener
    {
        private readonly CapabilityProfile _profile;
        private readonly DiagnosticsCollector _diag;

        public ResolvedValueFlattener(CapabilityProfile profile, DiagnosticsCollector diag)
        {
            _profile = profile;
            _diag = diag;
        }

        public List<OpResolved> Flatten(CamSetupSnapshot setup)
        {
            var result = new List<OpResolved>();
            var faceByTag = setup.Faces.ToDictionary(f => f.Tag);
            var edgeByTag = setup.Edges.ToDictionary(e => e.Tag);

            foreach (var op in PreorderOps(setup.ProgramRoot))
            {
                // 前置条件 6 处置：父组缺失或未在快照登记表中 → error 跳过该工序（I7）
                var missingParent = MissingParent(op, setup);
                if (missingParent != null)
                {
                    _diag.Error("MISSING_PARENT_GROUP",
                        string.Format("操作 {0} 父组缺失/未登记（{1}），跳过该工序（plan-exporter.md §3.2-6）", op.Name, missingParent));
                    continue;
                }

                // §4.4：类型映射。未知类型不跳过：other + warning，原始串保留进 nx_template
                TypeMapper.TryMap(op.TypeName, out var operationType, out var domain);
                if (domain == TypeMapper.Domains.Unknown)
                {
                    _diag.Warning("TYPE_UNMAPPED",
                        string.Format("操作 {0} 类型 {1} 未在映射表：operation_type=other，原始 typeName 保留进 nx_template（plan-exporter.md §4.4）", op.Name, op.TypeName));
                }

                // 前置条件 3 处置：许可缺失 → error 跳过该工序
                if (_profile.UnavailableLicenses.Contains(domain))
                {
                    _diag.Error("LICENSE_MISSING",
                        string.Format("操作 {0}（{1}）加工域 {2} 许可缺失，跳过该工序（plan-exporter.md §3.2-3）", op.Name, op.TypeName, domain));
                    continue;
                }

                var resolved = new OpResolved { Op = op, OperationType = operationType, Domain = domain };

                // §4.3 生效值拍平
                var unresolvedParams = new List<string>();
                foreach (var spec in ParamRegistry.All)
                {
                    // 前置条件 5 处置：能力探测失败 → warning 跳过该参数
                    if (_profile.UnsupportedParams.Contains(spec.Name))
                    {
                        _diag.Warning("CAPABILITY_UNSUPPORTED",
                            string.Format("参数 {0} 当前 NX 版本不可读（能力探测失败），跳过该参数（plan-exporter.md §3.2-5）", spec.Name));
                        continue;
                    }
                    var value = Resolve(op, spec, setup.TemplateDefaults);
                    if (value == null)
                    {
                        unresolvedParams.Add(spec.Name);
                        continue;   // §3.1d：字段可缺，诊断必增（见下方汇总）
                    }
                    var target = spec.Category == ParamCategory.Strategy ? resolved.Strategy : resolved.Technology;
                    target[spec.Name] = value;
                }
                if (unresolvedParams.Count > 0)
                {
                    _diag.Warning("UNRESOLVED_PARAMS",
                        string.Format("操作 {0} 以下参数沿继承链不可解析（缺省输出，重建侧将继承组默认值）：{1}（plan-exporter.md §3.3-2）",
                            op.Name, string.Join(", ", unresolvedParams)));
                }

                // §4.5：几何 Tag → Face/Edge 快照（Core 不计算，只匹配）
                foreach (var tag in op.GeometryTags)
                {
                    if (faceByTag.TryGetValue(tag, out var face))
                    {
                        resolved.Faces.Add(face);
                    }
                    else if (edgeByTag.TryGetValue(tag, out var edge))
                    {
                        resolved.Edges.Add(edge);
                    }
                    else
                    {
                        _diag.Warning("GEOM_TAG_UNRESOLVED",
                            string.Format("操作 {0} 关联几何 Tag {1} 无对应面/边，该 Tag 忽略", op.Name, tag));
                    }
                }

                result.Add(resolved);
            }
            return result;
        }

        /// <summary>Program 树前序工序序列（§3.1c 的输入保证：树序 = 刀路输出序）。</summary>
        private static IEnumerable<OperationSnapshot> PreorderOps(GroupSnapshot group)
        {
            foreach (var op in group.Operations)
            {
                yield return op;
            }
            foreach (var child in group.Children)
            {
                foreach (var op in PreorderOps(child))
                {
                    yield return op;
                }
            }
        }

        /// <summary>父组缺失/未登记检测（§3.2-6）。返回缺失描述，null = 齐全。</summary>
        private static string MissingParent(OperationSnapshot op, CamSetupSnapshot setup)
        {
            if (op.ProgramGroup == null) return "Program 组";
            if (op.MethodGroup == null || !setup.MethodGroups.Contains(op.MethodGroup)) return "Method 组";
            if (op.ToolGroup == null || !setup.ToolGroups.Contains(op.ToolGroup)) return "Tool 组";
            if (op.GeometryGroup == null || !setup.GeometryGroups.Contains(op.GeometryGroup)) return "Geometry 组";
            return null;
        }

        /// <summary>
        /// 继承解析：显式值优先；否则按 ParamRegistry.Source 决定的链序逐级查组参数，
        /// 最后查模板根。返回 null = 不可解析（调用侧落诊断，不伪造值）。
        /// </summary>
        private static object Resolve(OperationSnapshot op, ParamSpec spec, Dictionary<string, object> templateDefaults)
        {
            if (op.Params.TryGetValue(spec.Name, out var p) && p.IsSet)
            {
                return p.Value;
            }
            foreach (var group in InheritanceChain(op, spec))
            {
                if (group != null && group.Params.TryGetValue(spec.Name, out var value))
                {
                    return value;
                }
            }
            if (templateDefaults != null && templateDefaults.TryGetValue(spec.Name, out var templateValue))
            {
                return templateValue;
            }
            return null;
        }

        private static IEnumerable<GroupSnapshot> InheritanceChain(OperationSnapshot op, ParamSpec spec)
        {
            switch (spec.Source)
            {
                case ParamSource.Geometry:
                    yield return op.GeometryGroup;
                    yield return op.MethodGroup;
                    yield return op.ToolGroup;
                    yield break;
                case ParamSource.Tool:
                    yield return op.ToolGroup;
                    yield return op.MethodGroup;
                    yield return op.GeometryGroup;
                    yield break;
                default:   // 方法组 → 几何组 → 刀具组（§4.3 默认链）
                    yield return op.MethodGroup;
                    yield return op.GeometryGroup;
                    yield return op.ToolGroup;
                    yield break;
            }
        }
    }
}
