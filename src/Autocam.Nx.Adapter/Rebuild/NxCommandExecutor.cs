using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Create* 键语义（M2_Probe2 实测）= (setup 族, 视图类 subtype)——见 NxTemplateKeys。
    /// 模板注册表仅 GUI 会话加载（M0 实证批处理/run_journal 均缺失），执行验证在交互式 NX。
    /// </summary>
    public static class NxCommandExecutor
    {
        private static readonly NXOpen.CAM.OperationCollection.UseDefaultName UseDefaultOp =
            NXOpen.CAM.OperationCollection.UseDefaultName.False;
        private static readonly NXOpen.CAM.NCGroupCollection.UseDefaultName UseDefaultGroup =
            NXOpen.CAM.NCGroupCollection.UseDefaultName.False;

        public static void Execute(CAMSetup camSetup, IList<RebuildCommand> commands, DiagnosticsCollector diag, Action<string> progress = null)
        {
            var programByName = new Dictionary<string, NCGroup>();
            var methodByName = new Dictionary<string, NCGroup>();
            var toolByName = new Dictionary<string, NCGroup>();
            var geometryByName = new Dictionary<string, NCGroup>();

            for (var i = 0; i < commands.Count; i++)
            {
                progress?.Invoke("命令 [" + i + "/" + commands.Count + "] " + Describe(commands[i]));
                ExecuteOne(camSetup, commands[i], programByName, methodByName, toolByName, geometryByName, diag);
            }
        }

        /// <summary>GUI 会话下新 setup 自带模板默认组（ProgramOrder=[NONE,PROGRAM]、MachineMethod 含
        /// MILL_ROUGH…、Geometry=[NONE,MCS_MAIN]，M2 实测）——组命令按 find-or-create 复用同名组
        /// （复用更忠实：工序挂的组与原件同源于模板派生），plan 增组的才新建。</summary>
        private static NCGroup FindChildGroup(NCGroup parent, string name)
        {
            if (parent == null)
            {
                return null;
            }
            foreach (CAMObject member in parent.GetMembers())
            {
                if (member is NCGroup g && g.Name == name)
                {
                    return g;
                }
            }
            return null;
        }

        private static string Describe(RebuildCommand command)
        {
            switch (command)
            {
                case CreateCamSetupCommand _:
                    return "CreateCamSetup";
                case CreateMethodGroupCommand m:
                    return "CreateMethodGroup " + m.Name;
                case CreateToolGroupCommand t:
                    return "CreateToolGroup " + t.Name;
                case CreateGeometryGroupCommand g:
                    return "CreateGeometryGroup " + g.Name;
                case CreateProgramGroupCommand p:
                    return "CreateProgramGroup " + p.Name;
                case CreateOperationCommand o:
                    return "CreateOperation " + o.Name + " (" + o.TypeName + "/" + o.SubtypeName + ")";
                default:
                    return command.GetType().Name;
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
                    var methodRoot = camSetup.GetRoot(CAMSetup.View.MachineMethod);
                    var methodGroup = methodRoot != null && m.Name == methodRoot.Name
                        ? methodRoot   // 约定名 == 根组名（Unknown 域约定 "METHOD" 恰为模板根组）：根组即方法组
                        : FindChildGroup(methodRoot, m.Name);
                    if (methodGroup == null)
                    {
                        methodGroup = camSetup.CAMGroupCollection.CreateMethod(methodRoot,
                            NxTemplateKeys.SetupFamily, NxTemplateKeys.MethodGroup, UseDefaultGroup, m.Name);
                    }
                    methodByName[m.Name] = methodGroup;
                    break;

                case CreateToolGroupCommand t:
                    var toolRoot = camSetup.GetRoot(CAMSetup.View.MachineTool);
                    var toolType = t.Params != null && t.Params.TryGetValue("type", out var tv) && "DRILL".Equals(tv as string)
                        ? "DRILL"
                        : "MILL";
                    var toolSubtype = toolType == "DRILL" ? NxTemplateKeys.ToolGroupDrill : NxTemplateKeys.ToolGroupMill;
                    var toolGroup = FindChildGroup(toolRoot, t.Name)
                        ?? camSetup.CAMGroupCollection.CreateTool(toolRoot,
                            NxTemplateKeys.SetupFamily, toolSubtype, UseDefaultGroup, t.Name);
                    SetToolParams(camSetup, toolGroup, toolType, t.Params, diag);
                    toolByName[t.Name] = toolGroup;
                    break;

                case CreateGeometryGroupCommand g:
                    var geometryRoot = camSetup.GetRoot(CAMSetup.View.Geometry);
                    var geometryGroup = FindChildGroup(geometryRoot, g.Name)
                        ?? camSetup.CAMGroupCollection.CreateGeometry(geometryRoot,
                            NxTemplateKeys.SetupFamily, NxTemplateKeys.GeometryGroupMcs, UseDefaultGroup, g.Name);
                    SetGeometryParams(camSetup, geometryGroup, g);
                    geometryByName[g.Name] = geometryGroup;
                    break;

                case CreateProgramGroupCommand p:
                    var parent = p.ParentName == null
                        ? camSetup.GetRoot(CAMSetup.View.ProgramOrder)
                        : programByName[p.ParentName];
                    // plan workplan 根节点名 = 导出带入的 ProgramOrder 根组名（NC_PROGRAM）：
                    // 顶级组请求名 == 根组名 → 根组即目标组（与 METHOD 同理）
                    var programGroup = p.ParentName == null && parent != null && parent.Name == p.Name
                        ? parent
                        : FindChildGroup(parent, p.Name);
                    if (programGroup == null)
                    {
                        programGroup = camSetup.CAMGroupCollection.CreateProgram(parent,
                            NxTemplateKeys.SetupFamily, NxTemplateKeys.ProgramGroup, UseDefaultGroup, p.Name);
                    }
                    programByName[p.Name] = programGroup;
                    break;

                case CreateOperationCommand o:
                    var opSubtype = NxTemplateKeys.ResolveOperationSubtype(o.TypeName);
                    if (opSubtype == null)
                    {
                        diag.Error("OP_TYPE_UNMAPPED",
                            "工序 " + o.Name + " 类型 " + o.TypeName
                            + " 未入 NxTemplateKeys 映射表，跳过创建（执行忠实，不创建错类型）");
                        break;
                    }
                    var op = camSetup.CAMOperationCollection.Create(
                        programByName[o.ProgramGroupName],
                        methodByName[o.MethodGroupName],
                        toolByName[o.ToolGroupName],
                        geometryByName[o.GeometryGroupName],
                        NxTemplateKeys.SetupFamily, opSubtype, UseDefaultOp, o.Name);
                    SetOperationParams(camSetup, op, o.Params, diag);
                    break;
            }
        }

        // ---- 参数写（反射路径 = 读取侧镜像；缺字段零 Set——命令里没有的字段不碰）----

        private static void SetToolParams(CAMSetup camSetup, NCGroup toolGroup, string toolType, Dictionary<string, object> planParams, DiagnosticsCollector diag)
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
                        SetParamPath(builder, pair.Value, value, diag, pair.Key);
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

        private static void SetOperationParams(CAMSetup camSetup, NXOpen.CAM.Operation op, List<SetParam> planParams, DiagnosticsCollector diag)
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
                    SetParamPath(builder, path, param.Value, diag, param.Name);
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
        /// 叶子写：Inheritable*Builder.Value / 枚举 Parse（宽松匹配）/ bool / 复合（stepover 等递归）。
        /// 写失败 → warning + 跳过该参数，不阻断整条命令流（失败隔离）；plan″ 以缺字段显形。
        /// </summary>
        private static void SetLeaf(object leaf, object value, DiagnosticsCollector diag, string paramName)
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
                var parsed = ParseEnumValue(type, s);
                if (parsed == null)
                {
                    diag.Warning("PARAM_SET_FAILED",
                        string.Format("参数 {0} 值 {1} 无法映射到 NX 枚举 {2}（宽松匹配后仍无），跳过该参数（plan″ 将以缺字段显形）",
                            paramName, s, type.Name));
                    return;
                }
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
            // 复合对象（stepover{mode,value} 等）：递归子键（子键可能仍是裸值 → 走 SetParamPath）
            if (value is Dictionary<string, object> dict)
            {
                foreach (var pair in dict)
                {
                    SetParamPath(leaf, pair.Key, pair.Value, diag, paramName + "." + pair.Key);
                }
            }
        }

        /// <summary>
        /// 沿点分路径写参数（NxParamPaths 路径以 OperationBuilder 为根）。
        /// 末段叶子：引用包装（Inheritable*Builder/StepoverBuilder 等，带 Value 属性）
        /// → SetLeaf 原地写；裸值（枚举/double/bool/string——M3_Probe 实证 CutOrder 裸枚举、
        /// BoundaryInTol 裸 double）→ 装箱对象改不动，须经父属性直接写回。
        /// 路径不可达/复合体未实例化 → 跳过（plan″ 以缺字段显形，不静默伪造）。
        /// </summary>
        private static void SetParamPath(object root, string path, object value, DiagnosticsCollector diag, string paramName)
        {
            if (root == null || string.IsNullOrEmpty(path) || value == null)
            {
                return;
            }
            var segments = path.Split('.');
            object container = root;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                container = ReadSegment(container, segments[i]);
                if (container == null)
                {
                    return;
                }
            }
            var prop = container.GetType().GetProperty(segments[segments.Length - 1]);
            if (prop == null)
            {
                return;
            }
            var propType = prop.PropertyType;
            object leaf;
            try
            {
                leaf = prop.GetValue(container, null);
            }
            catch (Exception)
            {
                return;
            }
            if (leaf != null && !propType.IsValueType && propType != typeof(string))
            {
                // 引用叶子（Inheritable*Builder/StepoverBuilder 等包装）：原地写（旧 SetLeaf 路径）。
                // 注意不可检查 prop.CanWrite——NX builder 参数属性常为 get-only（M3 实测
                // MillCutParameters.PartStock 只读），检查会把本可写的包装短路。
                SetLeaf(leaf, value, diag, paramName);
                return;
            }
            if (value is Dictionary<string, object> dict)
            {
                if (leaf == null)
                {
                    return;   // 复合包装未实例化：不可构造，跳过
                }
                foreach (var pair in dict)
                {
                    SetParamPath(leaf, pair.Key, pair.Value, diag, paramName + "." + pair.Key);
                }
                return;
            }
            // 裸值/枚举（M3_Probe 实证 CutOrder 裸枚举、BoundaryInTol 裸 double）：
            // 装箱对象改不动，须经父属性写回（此处才需要 CanWrite）
            if (!prop.CanWrite)
            {
                return;
            }
            var converted = ConvertScalarFor(propType, value, diag, paramName);
            if (converted != null)
            {
                prop.SetValue(container, converted, null);
            }
        }

        private static object ReadSegment(object o, string name)
        {
            if (o == null)
            {
                return null;
            }
            try
            {
                var p = o.GetType().GetProperty(name);
                return p == null ? null : p.GetValue(o, null);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>裸值转换：数值/double/int、bool、string、枚举（宽松解析）；类型不合 → null（跳过）。</summary>
        private static object ConvertScalarFor(Type targetType, object value, DiagnosticsCollector diag, string paramName)
        {
            try
            {
                if (targetType.IsEnum)
                {
                    if (value is string s)
                    {
                        var parsed = ParseEnumValue(targetType, s);
                        if (parsed == null)
                        {
                            diag.Warning("PARAM_SET_FAILED",
                                string.Format("参数 {0} 值 {1} 无法映射到 NX 枚举 {2}（宽松匹配后仍无），跳过该参数（plan″ 将以缺字段显形）",
                                    paramName, s, targetType.Name));
                        }
                        return parsed;
                    }
                    if (value is int || value is long)
                    {
                        return Enum.ToObject(targetType, Convert.ToInt32(value));
                    }
                    return null;
                }
                if (targetType == typeof(double) && (value is double || value is int || value is long || value is float))
                {
                    return Convert.ToDouble(value);
                }
                if (targetType == typeof(int) && (value is int || value is long))
                {
                    return Convert.ToInt32(value);
                }
                if (targetType == typeof(bool) && value is bool)
                {
                    return value;
                }
                if (targetType == typeof(string) && value is string)
                {
                    return value;
                }
            }
            catch (Exception)
            {
                // 转换异常 → null → 跳过（缺字段显形）
            }
            return null;
        }

        /// <summary>
        /// 枚举宽松解析：plan 枚举为大写蛇形（schema 风格），NX 枚举成员可能是 Pascal/无分隔符
        /// （如 LEVEL_FIRST ↔ LevelFirst）——先精确 Parse，再按「去非字母数字 + 忽略大小写」等价匹配。
        /// </summary>
        private static object ParseEnumValue(Type enumType, string value)
        {
            try
            {
                return Enum.Parse(enumType, value, true);
            }
            catch (ArgumentException)
            {
                // 精确匹配失败 → 宽松匹配
            }
            var normalized = NormalizeEnumName(value);
            foreach (var name in Enum.GetNames(enumType))
            {
                if (string.Equals(NormalizeEnumName(name), normalized, StringComparison.Ordinal))
                {
                    return Enum.Parse(enumType, name, false);
                }
            }
            return null;
        }

        private static string NormalizeEnumName(string s)
        {
            return new string(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
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