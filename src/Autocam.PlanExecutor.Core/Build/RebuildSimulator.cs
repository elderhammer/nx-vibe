using System.Collections.Generic;
using Autocam.PlanExporter.Core.Dto;

namespace Autocam.PlanExecutor.Core.Build
{
    /// <summary>
    /// 命令序列 → 模拟快照 S′（plan-executor.md §4.4）。
    /// 内存状态上执行命令：四视图组树 + 工序（显式参数 = plan 出现字段）+
    /// 合成面（AnchorPoint → FaceSnapshot.Centroid）。
    /// PartName=plan.name、InputRef=plan.input_ref、TemplateDefaults 为空——
    /// 保证导出侧继承链解析结果 == plan 字段集（§3.1b 的机制来源）。
    /// S′ 是 round-trip 测试（Export(S′) ≡ plan）的输入。
    /// </summary>
    public static class RebuildSimulator
    {
        public static CamSetupSnapshot Run(IList<RebuildCommand> commands, string partName, string inputRef)
        {
            var setup = new CamSetupSnapshot { PartName = partName, InputRef = inputRef };
            var programByName = new Dictionary<string, GroupSnapshot>();
            var methodByName = new Dictionary<string, GroupSnapshot>();
            var toolByName = new Dictionary<string, GroupSnapshot>();
            var geometryByName = new Dictionary<string, GroupSnapshot>();

            foreach (var command in commands)
            {
                switch (command)
                {
                    case CreateCamSetupCommand _:
                        break;

                    case CreateMethodGroupCommand m:
                        var method = new GroupSnapshot { Kind = GroupKind.Method, Name = m.Name, DisplayName = m.Name };
                        setup.MethodGroups.Add(method);
                        methodByName[m.Name] = method;
                        break;

                    case CreateToolGroupCommand t:
                        var tool = new GroupSnapshot
                        {
                            Kind = GroupKind.Tool,
                            Name = t.Name,
                            DisplayName = t.Name,
                            Params = new Dictionary<string, object>(t.Params),
                        };
                        setup.ToolGroups.Add(tool);
                        toolByName[t.Name] = tool;
                        break;

                    case CreateGeometryGroupCommand g:
                        var geometry = new GroupSnapshot { Kind = GroupKind.Geometry, Name = g.Name, DisplayName = g.Name };
                        if (g.Origin != null) geometry.Params["origin"] = g.Origin;
                        if (g.ZAxis != null) geometry.Params["z_axis"] = g.ZAxis;
                        if (g.XAxis != null) geometry.Params["x_axis"] = g.XAxis;
                        if (g.SafePlaneZ.HasValue) geometry.Params["safe_plane_z"] = g.SafePlaneZ.Value;
                        if (g.FixtureOffset.HasValue) geometry.Params["fixture_offset"] = g.FixtureOffset.Value;
                        setup.GeometryGroups.Add(geometry);
                        geometryByName[g.Name] = geometry;
                        break;

                    case CreateProgramGroupCommand p:
                        var program = new GroupSnapshot { Kind = GroupKind.Program, Name = p.Name, DisplayName = p.Name };
                        if (p.ParentName == null)
                        {
                            setup.ProgramRoot = program;
                        }
                        else
                        {
                            programByName[p.ParentName].Children.Add(program);
                        }
                        programByName[p.Name] = program;
                        break;

                    case CreateOperationCommand o:
                        var op = new OperationSnapshot
                        {
                            Name = o.Name,
                            TypeName = o.TypeName,
                            SubtypeName = o.SubtypeName,
                            ProgramGroup = programByName[o.ProgramGroupName],
                            MethodGroup = methodByName[o.MethodGroupName],
                            ToolGroup = toolByName[o.ToolGroupName],
                            GeometryGroup = geometryByName[o.GeometryGroupName],
                        };
                        op.ProgramGroup.Operations.Add(op);
                        foreach (var p in o.Params)
                        {
                            op.Params[p.Name] = new OpParam { IsSet = true, Value = p.Value };
                        }
                        if (o.AnchorPoint != null)
                        {
                            var tag = o.Name + "_face";
                            setup.Faces.Add(new FaceSnapshot
                            {
                                Tag = tag,
                                Centroid = o.AnchorPoint,
                                Area = 0.0,
                                FaceType = "Synthetic",
                            });
                            op.GeometryTags.Add(tag);
                        }
                        break;
                }
            }
            return setup;
        }
    }
}
