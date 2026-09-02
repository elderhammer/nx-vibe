using System;
using System.Collections.Generic;
using System.Linq;
using Autocam.PlanExporter.Core.Dto;
using Autocam.PlanExporter.Core.Plan;
using Autocam.PlanExporter.Core.Policies;

namespace Autocam.PlanExporter.Core.Export
{
    /// <summary>
    /// §4.1 第 4 步 / §4.7：组装 plan 对象图并校验。
    /// - ID 分配：每次导出独立的单调递增计数器（I3；不透明字符串，格式不作合同）
    /// - plan_id：由输入派生（PartName），每次导出确定生成（§3.1e，进程级计数会破坏幂等）
    /// - setup 划分：按工序几何组首次出现序（§4.2）
    /// - workplan：Program 树前序投影，组节点 name+children，工序叶子 workingstep_ref（I4）
    /// - 引用闭合（后置条件 4）+ 双射覆盖（后置条件 5）+ ID 唯一（I3）校验，违规落 error 诊断
    /// 注意：组装期不使用任何 Dictionary 的迭代序——所有输出顺序都来自有序列表/前序，
    /// 这是 §3.1e 字节级确定性的来源之一。
    /// </summary>
    public sealed class PlanAssembler
    {
        private readonly DiagnosticsCollector _diag;

        public PlanAssembler(DiagnosticsCollector diag)
        {
            _diag = diag;
        }

        public PlanRoot Assemble(CamSetupSnapshot setup, IList<OpResolved> resolved, IDictionary<OperationSnapshot, double[]> anchors)
        {
            var plan = new PlanRoot
            {
                PlanId = "PLAN-" + setup.PartName,
                Name = setup.PartName,
                InputRef = setup.InputRef,
            };
            var ids = new IdAllocator();

            // 1. 刀具：按工序引用序去重（§4.5 最直填部分）
            var toolByGroup = new Dictionary<GroupSnapshot, ToolEntry>();
            foreach (var r in resolved)
            {
                if (toolByGroup.ContainsKey(r.Op.ToolGroup))
                {
                    continue;
                }
                var group = r.Op.ToolGroup;
                var entry = new ToolEntry { ToolId = ids.Next("T") };
                foreach (var field in ParamRegistry.ToolFields)
                {
                    if (!group.Params.TryGetValue(field, out var value))
                    {
                        _diag.Warning("TOOL_FIELD_MISSING",
                            string.Format("刀具组 {0} 缺 MVP 字段 {1}，缺省输出（nx-plugin-design.md §5）", group.Name, field));
                        continue;
                    }
                    ApplyToolField(entry, field, value);
                }
                toolByGroup[group] = entry;
                plan.Resources.Tools.Add(entry);
            }

            // 2. setups：按工序几何组首次出现序（§4.2 setup 划分）
            var setupByGroup = new Dictionary<GroupSnapshot, SetupEntry>();
            foreach (var r in resolved)
            {
                if (setupByGroup.ContainsKey(r.Op.GeometryGroup))
                {
                    continue;
                }
                var group = r.Op.GeometryGroup;
                var entry = new SetupEntry { SetupId = ids.Next("SETUP") };
                entry.Mcs = new McsEntry
                {
                    Origin = AsVector(group.Params, "origin"),
                    ZAxis = AsVector(group.Params, "z_axis"),
                    XAxis = AsVector(group.Params, "x_axis"),
                };
                if (group.Params.TryGetValue("safe_plane_z", out var safePlaneZ))
                {
                    entry.SafePlaneZ = Convert.ToDouble(safePlaneZ);
                }
                if (group.Params.TryGetValue("fixture_offset", out var fixtureOffset))
                {
                    entry.FixtureOffset = Convert.ToInt32(fixtureOffset);
                }
                setupByGroup[group] = entry;
                plan.Setups.Add(entry);
            }

            // 3. 每工序 → feature + operation + workingstep（§3.3-5 双射）
            var wsByOp = new Dictionary<OperationSnapshot, WorkingstepEntry>();
            foreach (var r in resolved)
            {
                var operationId = ids.Next("OP");
                var operation = new OperationEntry
                {
                    OperationId = operationId,
                    OperationType = r.OperationType,
                    NxTemplate = new NxTemplateEntry { Type = r.Op.TypeName, Subtype = r.Op.SubtypeName },
                    ToolRef = toolByGroup[r.Op.ToolGroup].ToolId,
                    Strategy = r.Strategy,
                    Technology = r.Technology,
                };
                var feature = new FeatureEntry
                {
                    FeatureId = ids.Next("F"),
                    FeatureType = "",   // 导出侧无法识别 AP224 类型，云端回填（见下方 INFO）
                };
                if (anchors.TryGetValue(r.Op, out var anchor))
                {
                    feature.GeometryRef = new GeometryRefEntry { AnchorPoint = anchor };
                }
                var ws = new WorkingstepEntry
                {
                    WorkingstepId = ids.Next("WS"),
                    FeatureRef = feature.FeatureId,
                    OperationRef = operationId,
                    SetupRef = setupByGroup[r.Op.GeometryGroup].SetupId,
                };
                plan.Operations.Add(operation);
                plan.Features.Add(feature);
                plan.Workingsteps.Add(ws);
                wsByOp[r.Op] = ws;
            }

            // 4. workplan：Program 树前序投影（§3.1c / I4）。被跳过的工序不出现（I7）
            plan.Workplan.Root = ProjectWorkplan(setup.ProgramRoot, wsByOp);
            plan.Workplan.Elements = FlattenPreorder(plan.Workplan.Root);

            // 5. feature_type 云端回填提示（固定一条 INFO，§2.1 关联几何口径）
            if (plan.Features.Count > 0)
            {
                _diag.Info("FEATURE_TYPE_PENDING",
                    "feature_type 为空串：导出侧无法识别 AP224 类型，待云端特征识别回填（plan-exporter.md §2.3 口径说明）");
            }

            // 6. 引用闭合 + 双射 + ID 唯一校验（后置条件 4/5、I3）
            VerifyClosureAndBijection(plan);

            // 7. 诊断入库（顺序 = 各阶段产生顺序，确定性）
            plan.Diagnostics.AddRange(_diag.Entries);
            return plan;
        }

        private static WorkplanNodeEntry ProjectWorkplan(GroupSnapshot group, IDictionary<OperationSnapshot, WorkingstepEntry> wsByOp)
        {
            var node = new WorkplanNodeEntry { Name = group.Name };
            foreach (var op in group.Operations)
            {
                if (wsByOp.TryGetValue(op, out var ws))
                {
                    node.Children.Add(new WorkplanNodeEntry { Name = op.Name, WorkingstepRef = ws.WorkingstepId });
                }
            }
            foreach (var child in group.Children)
            {
                node.Children.Add(ProjectWorkplan(child, wsByOp));
            }
            return node;
        }

        private static List<WorkplanNodeEntry> FlattenPreorder(WorkplanNodeEntry node)
        {
            var result = new List<WorkplanNodeEntry> { node };
            foreach (var child in node.Children)
            {
                result.AddRange(FlattenPreorder(child));
            }
            return result;
        }

        private void VerifyClosureAndBijection(PlanRoot plan)
        {
            // 后置条件 4：引用闭合
            var toolIds = new HashSet<string>(plan.Resources.Tools.Select(t => t.ToolId));
            var featureIds = new HashSet<string>(plan.Features.Select(f => f.FeatureId));
            var opIds = new HashSet<string>(plan.Operations.Select(o => o.OperationId));
            var wsIds = new HashSet<string>(plan.Workingsteps.Select(w => w.WorkingstepId));
            var setupIds = new HashSet<string>(plan.Setups.Select(s => s.SetupId));

            foreach (var op in plan.Operations)
            {
                if (op.ToolRef != null && !toolIds.Contains(op.ToolRef))
                {
                    _diag.Error("REFERENCE_CLOSURE_VIOLATED",
                        string.Format("工序 {0} 的 tool_ref 悬空：{1}（plan-exporter.md §3.3-4）", op.OperationId, op.ToolRef));
                }
            }
            foreach (var ws in plan.Workingsteps)
            {
                if (!opIds.Contains(ws.OperationRef))
                {
                    _diag.Error("REFERENCE_CLOSURE_VIOLATED",
                        string.Format("工步 {0} 的 operation_ref 悬空：{1}（plan-exporter.md §3.3-4）", ws.WorkingstepId, ws.OperationRef));
                }
                if (!featureIds.Contains(ws.FeatureRef))
                {
                    _diag.Error("REFERENCE_CLOSURE_VIOLATED",
                        string.Format("工步 {0} 的 feature_ref 悬空：{1}（plan-exporter.md §3.3-4）", ws.WorkingstepId, ws.FeatureRef));
                }
                if (!setupIds.Contains(ws.SetupRef))
                {
                    _diag.Error("REFERENCE_CLOSURE_VIOLATED",
                        string.Format("工步 {0} 的 setup_ref 悬空：{1}（plan-exporter.md §3.3-4）", ws.WorkingstepId, ws.SetupRef));
                }
            }
            foreach (var node in plan.Workplan.Elements.Where(e => e.WorkingstepRef != null))
            {
                if (!wsIds.Contains(node.WorkingstepRef))
                {
                    _diag.Error("REFERENCE_CLOSURE_VIOLATED",
                        string.Format("workplan 节点 {0} 的 workingstep_ref 悬空：{1}（plan-exporter.md §3.3-4）", node.Name, node.WorkingstepRef));
                }
            }

            // 后置条件 5：双射覆盖（每工序 ↔ 恰好一个 operation + 一个 workingstep + 一个 feature）
            if (plan.Operations.Count != plan.Workingsteps.Count || plan.Operations.Count != plan.Features.Count)
            {
                _diag.Error("BIJECTION_VIOLATED",
                    string.Format("条目数不等：operations={0}, workingsteps={1}, features={2}（plan-exporter.md §3.3-5）",
                        plan.Operations.Count, plan.Workingsteps.Count, plan.Features.Count));
            }

            // I3：ID 全局唯一
            var allIds = new List<string> { plan.PlanId };
            allIds.AddRange(plan.Setups.Select(s => s.SetupId));
            allIds.AddRange(plan.Resources.Tools.Select(t => t.ToolId));
            allIds.AddRange(plan.Features.Select(f => f.FeatureId));
            allIds.AddRange(plan.Operations.Select(o => o.OperationId));
            allIds.AddRange(plan.Workingsteps.Select(w => w.WorkingstepId));
            if (allIds.Distinct().Count() != allIds.Count)
            {
                _diag.Error("ID_UNIQUENESS_VIOLATED", "存在重复 ID（plan-exporter.md §3.4 I3）");
            }
        }

        private static double[] AsVector(IDictionary<string, object> groupParams, string key)
        {
            return groupParams.TryGetValue(key, out var value) ? (double[])value : null;
        }

        private static void ApplyToolField(ToolEntry entry, string field, object value)
        {
            switch (field)
            {
                case "type":
                    entry.Type = (string)value;
                    break;
                case "diameter":
                    entry.Diameter = Convert.ToDouble(value);
                    break;
                case "num_flutes":
                    entry.NumFlutes = Convert.ToInt32(value);
                    break;
                case "flute_length":
                    entry.FluteLength = Convert.ToDouble(value);
                    break;
                case "lower_corner_radius":
                    entry.LowerCornerRadius = Convert.ToDouble(value);
                    break;
            }
        }

        /// <summary>每次导出独立的 ID 分配器（跨调用不共享状态 → 幂等可复现）。</summary>
        private sealed class IdAllocator
        {
            private int _counter;

            public string Next(string prefix)
            {
                _counter++;
                return string.Format("{0}-{1:000}", prefix, _counter);
            }
        }
    }
}
