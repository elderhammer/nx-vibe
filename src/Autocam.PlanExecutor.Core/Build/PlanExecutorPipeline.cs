using System;
using System.Collections.Generic;
using System.Linq;
using Autocam.PlanExecutor.Core.Policies;
using Autocam.PlanExporter.Core.Dto;
using Autocam.PlanExporter.Core.Export;
using Autocam.PlanExporter.Core.Plan;
using Autocam.PlanExporter.Core.Policies;

namespace Autocam.PlanExecutor.Core.Build
{
    /// <summary>
    /// PlanExecutor 门面（类名不叫 PlanExecutor 以避开与命名空间 Autocam.PlanExecutor
    /// 的 C# 简单名冲突——dev-pattern.md 反面清单 #1），plan-executor.md §4.1 总流程：
    /// 1. 前置检查（§3.4-1/2/3，致命抛 BuildAbortedException）
    /// 2. 引用闭合 + 生存判定（§3.4-4/5，逐条目 error + 跳过）
    /// 3. 组命令生成（方法/刀具/几何/Program，§2.2 规范顺序）
    /// 4. 工序命令生成（workplan 叶子序，§3.3 继承语义：缺字段不下发）
    /// 5. 模拟 S′（RebuildSimulator）
    /// 6. 诊断入库
    /// 纯函数：输入 plan + 能力画像 → BuildResult；同输入必同输出（确定性）。
    /// </summary>
    public static class PlanExecutorPipeline
    {
        public static BuildResult Build(PlanRoot plan, CapabilityProfile profile)
        {
            // 1. 前置检查（致命项）
            if (plan == null)
            {
                throw new BuildAbortedException("前置条件 1 不满足：plan 为 null（plan-executor.md §3.4-1）");
            }
            if (plan.Operations.Count == 0)
            {
                throw new BuildAbortedException("前置条件 2 不满足：plan 无工序可建（plan-executor.md §3.4-2）");
            }
            if (plan.Workplan == null || plan.Workplan.Root == null)
            {
                throw new BuildAbortedException("前置条件 3 不满足：workplan.root 缺失（plan-executor.md §3.4-3）");
            }

            var result = new BuildResult();
            var diag = new DiagnosticsCollector();
            profile = profile ?? new CapabilityProfile();

            // 2. 索引（重复 ID → error 诊断，保留首个）
            var opById = Index(plan.Operations, o => o.OperationId, "operation_id", diag);
            var toolById = Index(plan.Resources.Tools, t => t.ToolId, "tool_id", diag);
            var setupById = Index(plan.Setups, s => s.SetupId, "setup_id", diag);
            var featureById = Index(plan.Features, f => f.FeatureId, "feature_id", diag);
            var wsById = Index(plan.Workingsteps, w => w.WorkingstepId, "workingstep_id", diag);

            // 3. workplan 前序遍历：Program 组命令 + 生存判定（§3.4-4/5）
            var programGroups = new List<CreateProgramGroupCommand>();
            var surviving = new List<SurvivingOp>();
            Walk(plan.Workplan.Root, null, programGroups, surviving, opById, toolById, setupById, featureById, wsById, diag);

            // 4. 组命令（§2.2 规范顺序：CamSetup → 方法 → 刀具 → 几何 → Program，分组产出不交错）
            result.Commands.Add(new CreateCamSetupCommand());

            var domainSeen = new HashSet<string>();
            foreach (var s in surviving)
            {
                if (domainSeen.Add(s.Domain))
                {
                    result.Commands.Add(new CreateMethodGroupCommand { Name = MethodGroupNaming.ForDomain(s.Domain) });
                }
            }
            var toolSeen = new HashSet<string>();
            foreach (var s in surviving)
            {
                if (toolSeen.Add(s.Op.ToolRef))
                {
                    result.Commands.Add(new CreateToolGroupCommand { Name = s.Op.ToolRef, Params = ToolParams(toolById[s.Op.ToolRef]) });
                }
            }
            var setupSeen = new HashSet<string>();
            foreach (var s in surviving)
            {
                if (setupSeen.Add(s.Ws.SetupRef))
                {
                    var setup = setupById[s.Ws.SetupRef];
                    result.Commands.Add(new CreateGeometryGroupCommand
                    {
                        Name = setup.SetupId,
                        Origin = setup.Mcs?.Origin,
                        ZAxis = setup.Mcs?.ZAxis,
                        XAxis = setup.Mcs?.XAxis,
                        SafePlaneZ = setup.SafePlaneZ,
                        FixtureOffset = setup.FixtureOffset,
                    });
                }
            }
            result.Commands.AddRange(programGroups);

            // 5. 工序命令（workplan 叶子序；§3.3：缺字段不下发，绝不伪造值）
            foreach (var s in surviving)
            {
                var command = new CreateOperationCommand
                {
                    Name = s.LeafName,
                    TypeName = s.TypeName,
                    SubtypeName = s.Op.NxTemplate?.Subtype ?? "",
                    ProgramGroupName = s.ProgramGroupName,
                    MethodGroupName = MethodGroupNaming.ForDomain(s.Domain),
                    ToolGroupName = s.Op.ToolRef,
                    GeometryGroupName = s.Ws.SetupRef,
                    AnchorPoint = s.Feature?.GeometryRef?.AnchorPoint,
                };
                foreach (var spec in ParamRegistry.All)
                {
                    var dict = spec.Category == ParamCategory.Strategy ? s.Op.Strategy : s.Op.Technology;
                    if (!dict.TryGetValue(spec.Name, out var value))
                    {
                        continue;   // 缺字段 → 不产生 Set 命令，NX 侧继承组/模板默认
                    }
                    if (profile.UnsupportedParams.Contains(spec.Name))
                    {
                        diag.Warning("CAPABILITY_UNSUPPORTED",
                            string.Format("参数 {0} 当前 NX 版本不支持，跳过下发（plan-executor.md §3.3）", spec.Name));
                        continue;
                    }
                    command.Params.Add(new SetParam { Name = spec.Name, Value = value });
                }
                result.Commands.Add(command);
            }

            // 6. 模拟 S′（§4.4）
            result.Simulated = RebuildSimulator.Run(result.Commands, plan.Name, plan.InputRef);

            // 7. 诊断入库
            result.Diagnostics.AddRange(diag.Entries);
            return result;
        }

        /// <summary>workplan 前序递归：组节点 → Program 组命令；叶子 → 引用闭合 + 生存判定。</summary>
        private static void Walk(
            WorkplanNodeEntry node,
            string parentGroupName,
            List<CreateProgramGroupCommand> programGroups,
            List<SurvivingOp> surviving,
            Dictionary<string, OperationEntry> opById,
            Dictionary<string, ToolEntry> toolById,
            Dictionary<string, SetupEntry> setupById,
            Dictionary<string, FeatureEntry> featureById,
            Dictionary<string, WorkingstepEntry> wsById,
            DiagnosticsCollector diag)
        {
            if (!string.IsNullOrEmpty(node.WorkingstepRef))
            {
                // 叶子：引用闭合 + 类型映射，任一失败 → error + 跳过该叶子（§3.4-4/5）
                if (!wsById.TryGetValue(node.WorkingstepRef, out var ws))
                {
                    diag.Error("REFERENCE_DANGLING",
                        string.Format("workplan 叶子 {0} 的 workingstep_ref 悬空：{1}，跳过该叶子（plan-executor.md §3.4-4）", node.Name, node.WorkingstepRef));
                    return;
                }
                if (!opById.TryGetValue(ws.OperationRef, out var op))
                {
                    diag.Error("REFERENCE_DANGLING",
                        string.Format("工步 {0} 的 operation_ref 悬空：{1}，跳过（plan-executor.md §3.4-4）", ws.WorkingstepId, ws.OperationRef));
                    return;
                }
                if (!toolById.ContainsKey(op.ToolRef))
                {
                    diag.Error("REFERENCE_DANGLING",
                        string.Format("工序 {0} 的 tool_ref 悬空：{1}，跳过（plan-executor.md §3.4-4）", op.OperationId, op.ToolRef));
                    return;
                }
                if (!setupById.TryGetValue(ws.SetupRef, out var setup))
                {
                    diag.Error("REFERENCE_DANGLING",
                        string.Format("工步 {0} 的 setup_ref 悬空：{1}，跳过（plan-executor.md §3.4-4）", ws.WorkingstepId, ws.SetupRef));
                    return;
                }
                if (!featureById.TryGetValue(ws.FeatureRef, out var feature))
                {
                    diag.Error("REFERENCE_DANGLING",
                        string.Format("工步 {0} 的 feature_ref 悬空：{1}，跳过（plan-executor.md §3.4-4）", ws.WorkingstepId, ws.FeatureRef));
                    return;
                }

                string typeName;
                string domain;
                if (op.OperationType == "other")
                {
                    // 近似工序：nx_template 原始 typeName 直落（nx-plugin-design.md §6）
                    if (string.IsNullOrEmpty(op.NxTemplate?.Type))
                    {
                        diag.Error("OPERATION_TYPE_UNMAPPABLE",
                            string.Format("工序 {0} operation_type=other 且无 nx_template.type，跳过（plan-executor.md §3.4-5）", op.OperationId));
                        return;
                    }
                    typeName = op.NxTemplate.Type;
                    domain = TypeMapper.Domains.Unknown;
                }
                else if (!TypeMapper.TryMapOperationType(op.OperationType, out typeName, out domain))
                {
                    diag.Error("OPERATION_TYPE_UNMAPPABLE",
                        string.Format("工序 {0} operation_type={1} 无法映射，跳过（plan-executor.md §3.4-5）", op.OperationId, op.OperationType));
                    return;
                }

                surviving.Add(new SurvivingOp
                {
                    LeafName = node.Name,
                    Op = op,
                    Ws = ws,
                    Feature = feature,
                    TypeName = typeName,
                    Domain = domain,
                    ProgramGroupName = parentGroupName,
                });
                return;
            }

            // 组节点：Program 组命令（父先于子），再递归子节点
            programGroups.Add(new CreateProgramGroupCommand { Name = node.Name, ParentName = parentGroupName });
            foreach (var child in node.Children)
            {
                Walk(child, node.Name, programGroups, surviving, opById, toolById, setupById, featureById, wsById, diag);
            }
        }

        /// <summary>刀具命令参数：MVP 刀具字段（ParamRegistry 顺序），plan 缺字段则不下发。</summary>
        private static Dictionary<string, object> ToolParams(ToolEntry tool)
        {
            var result = new Dictionary<string, object>();
            foreach (var field in ParamRegistry.ToolFields)
            {
                object value = null;
                switch (field)
                {
                    case "type": value = tool.Type; break;
                    case "diameter": value = tool.Diameter; break;
                    case "num_flutes": value = tool.NumFlutes; break;
                    case "flute_length": value = tool.FluteLength; break;
                    case "lower_corner_radius": value = tool.LowerCornerRadius; break;
                }
                if (value != null)
                {
                    result[field] = value;
                }
            }
            return result;
        }

        private static Dictionary<string, T> Index<T>(IEnumerable<T> items, Func<T, string> key, string keyName, DiagnosticsCollector diag)
        {
            var map = new Dictionary<string, T>();
            foreach (var item in items)
            {
                var k = key(item);
                if (map.ContainsKey(k))
                {
                    diag.Error("DUPLICATE_ID", string.Format("{0} 重复：{1}，保留首个（plan-executor.md §3.4-4）", keyName, k));
                }
                else
                {
                    map[k] = item;
                }
            }
            return map;
        }

        private sealed class SurvivingOp
        {
            public string LeafName { get; set; }
            public OperationEntry Op { get; set; }
            public WorkingstepEntry Ws { get; set; }
            public FeatureEntry Feature { get; set; }
            public string TypeName { get; set; }
            public string Domain { get; set; }
            public string ProgramGroupName { get; set; }
        }
    }
}
