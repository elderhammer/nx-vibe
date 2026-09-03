using System;
using System.Collections.Generic;
using Autocam.Nx.Adapter.Policies;
using Autocam.Plan.Core.Diagnostics;
using Autocam.Plan.Core.Dto;
using NXOpen;
using NXOpen.CAM;

namespace Autocam.Nx.Adapter.Export
{
    /// <summary>
    /// 导出侧适配器核心：NX CAMSetup → CamSetupSnapshot（nx-adapter.md §4.1）。
    /// 检测 vs 处置分离：只如实上报 NX 状态（组树/工序/生效值/几何），
    /// 所有跳过/降级决策由 Core 前置条件负责。会话只读（I1 执行者）：Builder 只 Get 不 Commit，用毕 Destroy。
    /// MVP 已知限制（决策 D-适配-2 镜像）：工序关联几何不读（GeometryTags/Faces/Edges 空表）。
    /// </summary>
    public static class NxSnapshotReader
    {
        public static CamSetupSnapshot Read(CAMSetup camSetup, string partName, string inputRef, DiagnosticsCollector diag)
        {
            var snapshot = new CamSetupSnapshot { PartName = partName, InputRef = inputRef };
            var groupMap = new Dictionary<NCGroup, GroupSnapshot>();

            snapshot.ProgramRoot = ReadProgramTree(camSetup, groupMap);
            CollectGroups(camSetup, CAMSetup.View.MachineMethod, snapshot.MethodGroups, groupMap, null, diag);
            CollectGroups(camSetup, CAMSetup.View.MachineTool, snapshot.ToolGroups, groupMap, ReadToolParams, diag);
            CollectGroups(camSetup, CAMSetup.View.Geometry, snapshot.GeometryGroups, groupMap, ReadGeometryParams, diag);

            ReadOperations(camSetup, snapshot, groupMap, diag);
            return snapshot;
        }

        // ---- 组树 ----

        private static GroupSnapshot ReadProgramTree(CAMSetup camSetup, Dictionary<NCGroup, GroupSnapshot> map)
        {
            var root = camSetup.GetRoot(CAMSetup.View.ProgramOrder);
            return WalkGroups(root, GroupKind.Program, map);
        }

        private static GroupSnapshot WalkGroups(NCGroup group, GroupKind kind, Dictionary<NCGroup, GroupSnapshot> map)
        {
            if (group == null)
            {
                return null;
            }
            var snapshot = new GroupSnapshot
            {
                Kind = kind,
                Name = group.Name,
                DisplayName = string.IsNullOrEmpty(group.UserName) ? group.Name : group.UserName,
            };
            map[group] = snapshot;
            foreach (CAMObject member in group.GetMembers())
            {
                var child = member as NCGroup;
                if (child != null)
                {
                    snapshot.Children.Add(WalkGroups(child, kind, map));
                }
            }
            return snapshot;
        }

        private delegate void GroupParamsReader(CAMSetup camSetup, NCGroup group, GroupSnapshot target, DiagnosticsCollector diag);

        private static void CollectGroups(
            CAMSetup camSetup,
            CAMSetup.View view,
            List<GroupSnapshot> target,
            Dictionary<NCGroup, GroupSnapshot> map,
            GroupParamsReader paramReader,
            DiagnosticsCollector diag)
        {
            var root = camSetup.GetRoot(view);
            CollectGroupsRec(camSetup, root, target, map, paramReader, diag);
        }

        private static void CollectGroupsRec(CAMSetup camSetup, NCGroup group, List<GroupSnapshot> target, Dictionary<NCGroup, GroupSnapshot> map, GroupParamsReader paramReader, DiagnosticsCollector diag)
        {
            if (group == null)
            {
                return;
            }
            var snapshot = new GroupSnapshot
            {
                Kind = GroupKind.Program,   // 占位，由调用侧覆盖
                Name = group.Name,
                DisplayName = string.IsNullOrEmpty(group.UserName) ? group.Name : group.UserName,
            };
            map[group] = snapshot;
            if (paramReader != null)
            {
                paramReader(camSetup, group, snapshot, diag);
            }
            target.Add(snapshot);
            foreach (CAMObject member in group.GetMembers())
            {
                var child = member as NCGroup;
                if (child != null)
                {
                    CollectGroupsRec(camSetup, child, target, map, paramReader, diag);
                }
            }
        }

        // ---- 组参数：几何组（MCS/安全平面/夹具偏置）----

        private static void ReadGeometryParams(CAMSetup camSetup, NCGroup group, GroupSnapshot target, DiagnosticsCollector diag)
        {
            MillOrientGeomBuilder builder = null;
            try
            {
                builder = camSetup.CAMGroupCollection.CreateMillOrientGeomBuilder(group);
                var mcs = builder.Mcs;
                if (mcs != null)
                {
                    var origin = ValueExtractor.ReadPath(mcs, "Origin");
                    var ox = ValueExtractor.ReadPath(origin, "X");
                    var oy = ValueExtractor.ReadPath(origin, "Y");
                    var oz = ValueExtractor.ReadPath(origin, "Z");
                    if (ox is double && oy is double && oz is double)
                    {
                        target.Params["origin"] = new[] { (double)ox, (double)oy, (double)oz };
                    }
                    // NXMatrix → Element → 内层矩阵（成员名随版本，逐候选反射读取）
                    var element = ValueExtractor.ReadPath(mcs, "Orientation.Element");
                    if (element == null)
                    {
                        element = ValueExtractor.ReadPath(mcs, "Orientation");
                    }
                    var zx = AxisComponent(element, "Z", "x");
                    var zy = AxisComponent(element, "Z", "y");
                    var zz = AxisComponent(element, "Z", "z");
                    var xx = AxisComponent(element, "X", "x");
                    var xy = AxisComponent(element, "X", "y");
                    var xz = AxisComponent(element, "X", "z");
                    if (zx.HasValue && zy.HasValue && zz.HasValue)
                    {
                        target.Params["z_axis"] = new[] { zx.Value, zy.Value, zz.Value };
                    }
                    if (xx.HasValue && xy.HasValue && xz.HasValue)
                    {
                        target.Params["x_axis"] = new[] { xx.Value, xy.Value, xz.Value };
                    }
                }
                try
                {
                    target.Params["fixture_offset"] = builder.FixtureOffsetBuilder.Value;
                }
                catch (Exception)
                {
                    // 无夹具偏置：缺项
                }
                var clearance = builder.TransferClearanceBuilder;
                if (clearance != null)
                {
                    var planeType = clearance.ClearanceType;
                    if (planeType == NcmClearanceBuilder.ClearanceTypes.Plane)
                    {
                        target.Params["safe_plane_z"] = clearance.SafeDistance;
                    }
                }
            }
            catch (Exception)
            {
                // 非 MCS 类几何组（WORKPIECE/MILL_AREA 等）：无 MCS 参数，不告警（模板树常态）
            }
            finally
            {
                if (builder != null)
                {
                    builder.Destroy();
                }
            }
        }

        // ---- 组参数：刀具组 ----

        /// <summary>矩阵元素反射读取：候选成员名（内层矩阵成员名随 NX 版本微调）。</summary>
        private static double? AxisComponent(object element, string row, string axis)
        {
            foreach (var name in new[] { row + axis, row + axis.ToUpperInvariant() })
            {
                var v = ValueExtractor.ReadPath(element, name);
                if (v is double d)
                {
                    return d;
                }
            }
            return null;
        }

        private static void ReadToolParams(CAMSetup camSetup, NCGroup group, GroupSnapshot target, DiagnosticsCollector diag)
        {
            if (TryReadTool(camSetup.CAMGroupCollection.CreateMillToolBuilder, group, target, "END_MILL", null))
            {
                return;
            }
            if (TryReadTool(camSetup.CAMGroupCollection.CreateDrillStdToolBuilder, group, target, "DRILL", null))
            {
                return;
            }
            // 用户自定义成形刀（M3_Probe G 段实测：MILL_USER_DEFINED 组唯一可用工厂）。
            // type 近似 END_MILL（plan 合同无成形刀类型；近似在导出侧归一 + warning 绝不静默，
            // 重建侧以真实 MILL 类型落地——nx-plugin-design §6 近似工序口径）；diameter 读
            // HelicalDiameter（成形刀的实际切削直径，G1 实测 TlDiameterBuilder=0 而它=90）。
            if (TryReadTool(camSetup.CAMGroupCollection.CreateMillFormToolBuilder, group, target, "END_MILL", "HelicalDiameter"))
            {
                diag.Warning("TOOL_APPROX_FORM_MILL",
                    string.Format("刀具组 {0} 为用户自定义成形刀，type 近似 END_MILL（原始类型见 GetNameOfType），diameter 取 HelicalDiameter", group.Name));
                return;
            }
            diag.Warning("TOOL_BUILDER_FAILED",
                string.Format("刀具组 {0} 无法取 MillToolBuilder/DrillStdToolBuilder/MillFormToolBuilder，刀具参数缺项（nx-adapter.md §2.1）", group.Name));
        }

        private delegate object ToolBuilderFactory(NCGroup group);

        private static bool TryReadTool(ToolBuilderFactory factory, NCGroup group, GroupSnapshot target, string typeName, string diameterPathOverride)
        {
            object builder = null;
            try
            {
                builder = factory(group);
                target.Params["type"] = typeName;
                foreach (var pair in NxParamPaths.Tool)
                {
                    var path = pair.Key == "diameter" && diameterPathOverride != null ? diameterPathOverride : pair.Value;
                    var leaf = ValueExtractor.ReadPath(builder, path);
                    var value = ValueExtractor.Extract(leaf);
                    if (value != null)
                    {
                        target.Params[pair.Key] = value;
                    }
                }
                return true;
            }
            catch (Exception)
            {
                return false;   // 非该类型刀具组
            }
            finally
            {
                if (builder != null)
                {
                    var destroy = builder.GetType().GetMethod("Destroy");
                    if (destroy != null)
                    {
                        try
                        {
                            destroy.Invoke(builder, null);
                        }
                        catch (Exception)
                        {
                            // Destroy 失败不阻断
                        }
                    }
                }
            }
        }

        // ---- 工序 ----

        private static void ReadOperations(CAMSetup camSetup, CamSetupSnapshot snapshot, Dictionary<NCGroup, GroupSnapshot> map, DiagnosticsCollector diag)
        {
            foreach (NXOpen.CAM.Operation op in camSetup.CAMOperationCollection.ToArray())
            {
                var opSnapshot = new OperationSnapshot { Name = op.Name, SubtypeName = "" };
                ReadParams(camSetup, op, opSnapshot, diag);

                var pp = op.ParentProgramOrder;
                var pm = op.ParentMachineMethod;
                var pt = op.ParentMachineTool;
                var pg = op.ParentGeometry;

                GroupSnapshot program;
                map.TryGetValue(pp, out program);
                opSnapshot.ProgramGroup = program;
                GroupSnapshot method;
                map.TryGetValue(pm, out method);
                opSnapshot.MethodGroup = method;
                GroupSnapshot tool;
                map.TryGetValue(pt, out tool);
                opSnapshot.ToolGroup = tool;
                GroupSnapshot geometry;
                map.TryGetValue(pg, out geometry);
                opSnapshot.GeometryGroup = geometry;

                if (program != null)
                {
                    program.Operations.Add(opSnapshot);
                }
            }
        }

        private static void ReadParams(CAMSetup camSetup, NXOpen.CAM.Operation op, OperationSnapshot target, DiagnosticsCollector diag)
        {
            OperationBuilder builder = null;
            try
            {
                builder = camSetup.CAMOperationCollection.CreateBuilder(op);
                var typeName = TypeNameResolver.Resolve(builder);
                target.TypeName = typeName == "other" ? "UNKNOWN_" + TypeNameResolver.BuilderTypeName(builder) : typeName;
                if (typeName == "other")
                {
                    diag.Warning("TYPE_UNMAPPED",
                        string.Format("工序 {0} 的 Builder 类型 {1} 未入 TypeNameResolver 表，typeName 落 UNKNOWN_*（TypeMapper 将落 other + 保留原始串）",
                            op.Name, TypeNameResolver.BuilderTypeName(builder)));
                }
                foreach (var pair in NxParamPaths.Operation)
                {
                    var leaf = ValueExtractor.ReadPath(builder, pair.Value);
                    var value = ValueExtractor.Extract(leaf);
                    if (value != null)
                    {
                        target.Params[pair.Key] = new OpParam { IsSet = true, Value = value };   // D-适配-1：直读生效值全量
                    }
                }
            }
            catch (Exception ex)
            {
                diag.Warning("OP_BUILDER_FAILED",
                    string.Format("工序 {0} 的 Builder 创建/读取失败：{1}，参数缺项（nx-adapter.md §2.1 绝不伪造值）", op.Name, ex.Message));
            }
            finally
            {
                if (builder != null)
                {
                    try
                    {
                        builder.Destroy();
                    }
                    catch (Exception)
                    {
                        // Destroy 失败不阻断（会话只读纪律的最佳努力）
                    }
                }
            }
        }

    }
}
