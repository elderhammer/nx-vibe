using System;
using System.Collections.Generic;
using System.Reflection;
using Autocam.Nx.Adapter.Export;
using Autocam.Nx.Adapter.Policies;
using Autocam.Plan.Core.Diagnostics;
using Autocam.PlanExecutor.Core.Build;
using NXOpen;
using NXOpen.CAM;

namespace Autocam.Nx.Adapter.Rebuild
{
    /// <summary>
    /// 执行侧适配器：RebuildCommand 序列 → NXOpen 调用（nx-adapter.md §4.2），
    /// 语义基准 = RebuildSimulator（命令同构执行）。执行忠实：缺字段零 Set 调用。
    /// ⚠ 批处理限制（M0 实测）：对象模板注册表不加载，组/工序 Create 在批处理下不可行——
    /// 本类编译级验证，执行验证在交互式 NX（核对清单 M2 节）。
    /// </summary>
    public static class NxCommandExecutor
    {
        private static readonly NXOpen.CAM.OperationCollection.UseDefaultName UseDefaultOp =
            NXOpen.CAM.OperationCollection.UseDefaultName.False;
        private static readonly NXOpen.CAM.NCGroupCollection.UseDefaultName UseDefaultGroup =
            NXOpen.CAM.NCGroupCollection.UseDefaultName.False;

        public static void Execute(CAMSetup camSetup, IList<RebuildCommand> commands, DiagnosticsCollector diag)
        {
            var programByName = new Dictionary<string, NCGroup>();
            var methodByName = new Dictionary<string, NCGroup>();
            var toolByName = new Dictionary<string, NCGroup>();
            var geometryByName = new Dictionary<string, NCGroup>();

            foreach (var command in commands)
            {
                ExecuteOne(camSetup, command, programByName, methodByName, toolByName, geometryByName, diag);
            }
        }

        private static void ExecuteOne(
            CAMSetup camSetup,
            RebuildCommand command,
            Dictionary<string, NCGroup> programByName,
            Dictionary<string, NCGroup> methodByName,
            Dictionary<string, NCGroup> toolByName,
            Dictionary<string, NCGroup> geometryByName,
            DiagnosticsCollector diag)
        {
            switch (command)
            {
                case CreateCamSetupCommand _:
                    break;   // CAMSetup 由宿主引导创建（SessionBootstrap）

                case CreateMethodGroupCommand m:
                    methodByName[m.Name] = camSetup.CAMGroupCollection.CreateMethod(
                        camSetup.GetRoot(CAMSetup.View.MachineMethod), "MILL_METHOD", "", UseDefaultGroup, m.Name);
                    break;

                case CreateToolGroupCommand t:
                    var toolType = t.Params != null && t.Params.TryGetValue("type", out var tv) && "DRILL".Equals(tv as string)
                        ? "DRILL"
                        : "MILL";
                    var toolGroup = camSetup.CAMGroupCollection.CreateTool(
                        camSetup.GetRoot(CAMSetup.View.MachineTool), toolType, "", UseDefaultGroup, t.Name);
                    SetToolParams(camSetup, toolGroup, toolType, t.Params);
                    toolByName[t.Name] = toolGroup;
                    break;

                case CreateGeometryGroupCommand g:
                    var geometryGroup = camSetup.CAMGroupCollection.CreateGeometry(
                        camSetup.GetRoot(CAMSetup.View.Geometry), "MCS", "", UseDefaultGroup, g.Name);
                    SetGeometryParams(camSetup, geometryGroup, g);
                    geometryByName[g.Name] = geometryGroup;
                    break;

                case CreateProgramGroupCommand p:
                    var parent = p.ParentName == null
                        ? camSetup.GetRoot(CAMSetup.View.ProgramOrder)
                        : programByName[p.ParentName];
                    programByName[p.Name] = camSetup.CAMGroupCollection.CreateProgram(parent, "PROGRAM", "", UseDefaultGroup, p.Name);
                    break;

                case CreateOperationCommand o:
                    var op = camSetup.CAMOperationCollection.Create(
                        programByName[o.ProgramGroupName],
                        methodByName[o.MethodGroupName],
                        toolByName[o.ToolGroupName],
                        geometryByName[o.GeometryGroupName],
                        o.TypeName, o.SubtypeName, UseDefaultOp, o.Name);
                    SetOperationParams(camSetup, op, o.Params);
                    break;
            }
        }

        // ---- 参数写（反射路径 = 读取侧镜像；缺字段零 Set——命令里没有的字段不碰）----

        private static void SetToolParams(CAMSetup camSetup, NCGroup toolGroup, string toolType, Dictionary<string, object> planParams)
        {
            object builder = null;
            try
            {
                builder = toolType == "DRILL"
                    ? camSetup.CAMGroupCollection.CreateDrillStdToolBuilder(toolGroup)
                    : camSetup.CAMGroupCollection.CreateMillToolBuilder(toolGroup);
                if (planParams == null)
                {
                    return;
                }
                foreach (var pair in NxParamPaths.Tool)
                {
                    if (planParams.TryGetValue(pair.Key, out var value))
                    {
                        SetLeaf(ValueExtractor.ReadPath(builder, pair.Value), value);
                    }
                }
                CommitAndDestroy(builder);
                builder = null;
            }
            finally
            {
                if (builder != null)
                {
                    DestroyQuietly(builder);
                }
            }
        }

        private static void SetGeometryParams(CAMSetup camSetup, NCGroup geometryGroup, CreateGeometryGroupCommand g)
        {
            MillOrientGeomBuilder builder = null;
            try
            {
                builder = camSetup.CAMGroupCollection.CreateMillOrientGeomBuilder(geometryGroup);
                if (g.Origin != null && g.ZAxis != null && g.XAxis != null)
                {
                    SetMcsReflective(builder, g.Origin, g.ZAxis, g.XAxis);   // 反射设置（CCS 构造签名随版本，GUI 实测核对）
                }
                if (g.FixtureOffset.HasValue)
                {
                    builder.FixtureOffsetBuilder.Value = g.FixtureOffset.Value;
                }
                if (g.SafePlaneZ.HasValue)
                {
                    var clearance = builder.TransferClearanceBuilder;
                    clearance.ClearanceType = NcmClearanceBuilder.ClearanceTypes.Plane;
                    clearance.SafeDistance = g.SafePlaneZ.Value;
                }
                builder.Commit();
                builder.Destroy();
                builder = null;
            }
            finally
            {
                if (builder != null)
                {
                    DestroyQuietly(builder);
                }
            }
        }

        /// <summary>
        /// MCS 反射设置：读 builder.Mcs 现有 CCS，写 Origin 属性 + 内层矩阵元素（Xx..Zz 候选名）。
        /// 失败 → 跳过 MCS（缺项由重建侧导出时的继承/缺项诊断显形，绝不静默伪造）。
        /// </summary>
        private static void SetMcsReflective(MillOrientGeomBuilder builder, double[] origin, double[] zAxis, double[] xAxis)
        {
            var mcs = builder.Mcs;
            if (mcs == null)
            {
                return;   // 批处理/GUI 差异下取不到 CCS：跳过（M2 核对清单点 5）
            }
            var originProp = mcs.GetType().GetProperty("Origin");
            if (originProp != null && originProp.CanWrite)
            {
                var point = Activator.CreateInstance(originProp.PropertyType, origin[0], origin[1], origin[2]);
                originProp.SetValue(mcs, point, null);
            }
            var orientProp = mcs.GetType().GetProperty("Orientation");
            var matrix = orientProp != null ? orientProp.GetValue(mcs, null) : null;
            var elementProp = matrix != null ? matrix.GetType().GetProperty("Element") : null;
            var element = elementProp != null ? elementProp.GetValue(matrix, null) : matrix;
            if (element != null)
            {
                SetAxisReflective(element, "X", xAxis);
                SetAxisReflective(element, "Z", zAxis);
            }
        }

        private static void SetAxisReflective(object element, string row, double[] values)
        {
            var names = new[] { row + "x", row + "X", row + "y", row + "Y", row + "z", row + "Z" };
            for (var i = 0; i < 3; i++)
            {
                var prop = element.GetType().GetProperty(names[i * 2]);
                if (prop == null)
                {
                    prop = element.GetType().GetProperty(names[i * 2 + 1]);
                }
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(element, values[i], null);
                }
            }
        }

        private static void SetOperationParams(CAMSetup camSetup, NXOpen.CAM.Operation op, List<SetParam> planParams)
        {
            OperationBuilder builder = null;
            try
            {
                builder = camSetup.CAMOperationCollection.CreateBuilder(op);
                foreach (var param in planParams)
                {
                    if (!NxParamPaths.Operation.TryGetValue(param.Name, out var path))
                    {
                        continue;   // 未入表字段：不 Set（继承语义——plan-executor.md §3.3）
                    }
                    SetLeaf(ValueExtractor.ReadPath(builder, path), param.Value);
                }
                builder.Commit();
                builder.Destroy();
                builder = null;
            }
            finally
            {
                if (builder != null)
                {
                    DestroyQuietly(builder);
                }
            }
        }

        /// <summary>叶子写：Inheritable*Builder.Value / 枚举 Parse / bool / 复合（stepover 等递归）。</summary>
        private static void SetLeaf(object leaf, object value)
        {
            if (leaf == null || value == null)
            {
                return;
            }
            var type = leaf.GetType();
            if (value is double || value is int || value is long || value is float)
            {
                var v = Convert.ToDouble(value);
                var valueProp = type.GetProperty("Value");
                if (valueProp != null && valueProp.PropertyType == typeof(double))
                {
                    valueProp.SetValue(leaf, v, null);
                    return;
                }
                if (valueProp != null && valueProp.PropertyType == typeof(int))
                {
                    valueProp.SetValue(leaf, Convert.ToInt32(value), null);
                    return;
                }
                return;
            }
            if (value is string s && type.IsEnum)
            {
                var parsed = Enum.Parse(type, s, true);
                var prop = leaf.GetType().GetProperty("Value");
                if (prop != null)
                {
                    prop.SetValue(leaf, parsed, null);
                }
                return;
            }
            if (value is bool b)
            {
                var prop = type.GetProperty("Value");
                if (prop != null && prop.PropertyType == typeof(bool))
                {
                    prop.SetValue(leaf, b, null);
                }
            }
            // 复合对象（stepover{mode,value} 等）：递归子键（点分路径由映射表覆盖到叶子层）
            if (value is Dictionary<string, object> dict)
            {
                foreach (var pair in dict)
                {
                    var sub = ValueExtractor.ReadPath(leaf, pair.Key);
                    SetLeaf(sub, pair.Value);
                }
            }
        }

        private static void CommitAndDestroy(object builder)
        {
            builder.GetType().GetMethod("Commit").Invoke(builder, null);
            builder.GetType().GetMethod("Destroy").Invoke(builder, null);
        }

        private static void DestroyQuietly(object builder)
        {
            try
            {
                builder.GetType().GetMethod("Destroy").Invoke(builder, null);
            }
            catch (Exception)
            {
                // Destroy 失败不阻断
            }
        }

    }
}