using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Autocam.Nx.Adapter.Policies;
using Autocam.Plan.Core.Diagnostics;
using Autocam.Plan.Core.Dto;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;

namespace Autocam.Nx.Adapter.Export
{
    /// <summary>
    /// M4 几何写探针 v3（GUI 会话执行，零 UI 操作）：程序化构造「含显式面几何」的 ground truth。
    /// 实证链：①模板件无体且其工序无显式几何（读探针 v1-v4 + 写探针 v1 bodies=0）；
    /// ②op Create 传 subtype=null 会原生内存违例（v2——NxTemplateKeys 键表 key=plan 导出类型、
    /// value=模板注册 subtype，FACE_MILL_ZIGZAG 是值非键，ResolveOperationSubtype 须用
    /// plan 类型入参）；③带 CAM.Geometry 五角色的 Builder 是 VolumeBased25D（POCKETING 系）
    /// 而非 Facing 系（读探针 Pass A 实证）。
    /// v3 流程：父组/刀具 → 先建 3 个 POCKETING 工序（无体上下文，与 M2 实证一致）→
    /// 方块体 → 选面 → 逐 op 几何角色 AppendGeometrySet 挂面 → Commit → 回读角色矩阵。
    /// 矩阵 = M4a 读侧 / M4b 写侧同表。任何失败如实落输出。
    /// </summary>
    public static class NxGeoWriterProbe
    {
        public static string Run(CAMSetup camSetup, StringBuilder sb, UFSession uf, out Dictionary<string, List<Face>> attached)
        {
            attached = new Dictionary<string, List<Face>>();
            sb.AppendLine("== M4 几何写探针 v3（自建 ground truth）==");
            var session = Session.GetSession();
            var work = session.Parts.Work;
            var createdOps = new List<NXOpen.CAM.Operation>();

            // ---- 1. 父组定位：GUI 新 setup 默认组 + 自建刀具组 ----
            var programRoot = camSetup.GetRoot(CAMSetup.View.ProgramOrder);
            var methodRoot = camSetup.GetRoot(CAMSetup.View.MachineMethod);
            var toolRoot = camSetup.GetRoot(CAMSetup.View.MachineTool);
            var geomRoot = camSetup.GetRoot(CAMSetup.View.Geometry);
            var program = FindChild(programRoot, "PROGRAM") ?? programRoot;
            var method = FindChild(methodRoot, "MILL_ROUGH") ?? methodRoot;
            NCGroup tool = FindChild(toolRoot, "T-M4");
            if (tool == null)
            {
                try
                {
                    tool = camSetup.CAMGroupCollection.CreateTool(toolRoot,
                        NxTemplateKeys.SetupFamily, NxTemplateKeys.ToolGroupMill,
                        NCGroupCollection.UseDefaultName.False, "T-M4");
                    sb.AppendLine("刀具组 T-M4 已建");
                }
                catch (Exception ex)
                {
                    sb.AppendLine("刀具组创建失败: " + Short(ex));
                }
            }
            var geometry = FindChild(geomRoot, "MCS_MAIN") ?? FindChild(geomRoot, "MCS") ?? geomRoot;
            sb.AppendLine("父组: program=" + (program == null ? "(null)" : program.Name)
                + " method=" + (method == null ? "(null)" : method.Name)
                + " tool=" + (tool == null ? "(null)" : tool.Name)
                + " geometry=" + (geometry == null ? "(null)" : geometry.Name));
            if (program == null || method == null || tool == null || geometry == null)
            {
                sb.AppendLine("FATAL: 父组缺失");
                return sb.ToString();
            }

            // ---- 2. 先建 3 个 POCKETING 工序（VolumeBased25D 系，带五几何角色；无体上下文与 M2 实证一致）----
            var attachNames = new[] { "M4_PKT_CUT_TOP", "M4_PKT_PART_WALL", "M4_PKT_CUT_WALL" };
            foreach (var name in attachNames)
            {
                try
                {
                    var subtype = NxTemplateKeys.ResolveOperationSubtype("UNKNOWN_VolumeBased25DMillingOperationBuilder");
                    if (subtype == null)
                    {
                        sb.AppendLine("建 " + name + " 失败: 键表无 UNKNOWN_VolumeBased25DMillingOperationBuilder 映射");
                        continue;
                    }
                    var op = camSetup.CAMOperationCollection.Create(
                        program, method, tool, geometry,
                        NxTemplateKeys.SetupFamily, subtype,
                        NXOpen.CAM.OperationCollection.UseDefaultName.False, name);
                    if (op == null)
                    {
                        sb.AppendLine("建 " + name + " 失败: Create 返回 null（模板注册表缺失？）");
                        continue;
                    }
                    createdOps.Add(op);
                    sb.AppendLine("op 已建: " + op.Name);
                }
                catch (Exception ex)
                {
                    sb.AppendLine("建 " + name + " 失败: " + Short(ex));
                }
            }

            // ---- 3. 程序化方块体（100×60×20，原点 0,0,0）----
            try
            {
                object bb = null;
                try
                {
                    bb = work.Features.CreateBlockFeatureBuilder(null);
                    var setOrigin = bb.GetType().GetMethod("SetOriginAndLengths");
                    setOrigin.Invoke(bb, new object[] { new Point3d(0, 0, 0), "100", "60", "20" });
                    bb.GetType().GetMethod("Commit").Invoke(bb, null);
                    sb.AppendLine("方块体已建: ok");
                }
                finally
                {
                    if (bb != null)
                    {
                        try
                        {
                            bb.GetType().GetMethod("Destroy").Invoke(bb, null);
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("方块体创建失败: " + Short(ex));
                return sb.ToString();
            }

            // ---- 4. 选面：顶面（+Z 平面）与一个竖直壁面 ----
            Face topFace = null;
            Face wallFace = null;
            var bodies = work.Bodies.ToArray();
            sb.AppendLine("bodies: " + bodies.Length);
            double bestZ = double.NegativeInfinity;
            foreach (var body in bodies)
            {
                foreach (Face face in body.GetFaces())
                {
                    int type;
                    var point = new double[3];
                    var dir = new double[3];
                    var box = new double[6];
                    try
                    {
                        double r0, r1;
                        int normDir;
                        uf.Modl.AskFaceData(face.Tag, out type, point, dir, box, out r0, out r1, out normDir);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    if (type == 22 && dir[2] > 0.5)   // 22 = bounded plane；法向 +Z
                    {
                        if (topFace == null || point[2] > bestZ)
                        {
                            topFace = face;
                            bestZ = point[2];
                        }
                    }
                    else if (type == 22 && Math.Abs(dir[2]) < 0.3 && wallFace == null)
                    {
                        wallFace = face;
                    }
                }
            }
            sb.AppendLine("topFace: " + (topFace == null ? "(null)" : topFace.Tag.ToString())
                + " wallFace: " + (wallFace == null ? "(null)" : wallFace.Tag.ToString()));
            if (topFace == null)
            {
                sb.AppendLine("FATAL: 未找到 +Z 顶面");
                return sb.ToString();
            }

            // ---- 5. 逐 op 挂面（角色矩阵）：CUT_TOP: CutArea←顶面；PART_WALL: Part←壁面；CUT_WALL: CutArea←壁面 ----
            var attachPlan = new[]
            {
                new Attach { Name = "M4_PKT_CUT_TOP", Role = "CutAreaGeometry", Face = topFace },
                new Attach { Name = "M4_PKT_PART_WALL", Role = "PartGeometry", Face = wallFace },
                new Attach { Name = "M4_PKT_CUT_WALL", Role = "CutAreaGeometry", Face = wallFace },
            };
            foreach (var a in attachPlan)
            {
                var op = createdOps.Find(o => o.Name == a.Name);
                if (op == null)
                {
                    sb.AppendLine("== " + a.Name + " 未建成，跳过挂接");
                    continue;
                }
                sb.AppendLine("== 挂接 " + a.Name + " (role=" + a.Role + ")");
                OperationBuilder builder = null;
                try
                {
                    builder = camSetup.CAMOperationCollection.CreateBuilder(op);
                    sb.AppendLine("  builder: " + builder.GetType().Name);
                    var prop = builder.GetType().GetProperty(a.Role);
                    if (prop == null)
                    {
                        sb.AppendLine("  角色属性不存在: " + a.Role + "（矩阵记 FAIL）");
                        continue;
                    }
                    NXOpen.CAM.Geometry geom;
                    try
                    {
                        geom = prop.GetValue(builder, null) as NXOpen.CAM.Geometry;
                        sb.AppendLine("  角色获取: " + (geom == null ? "(null)" : "ok"));
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("  角色获取失败: " + Full(ex));
                        continue;
                    }
                    if (geom == null)
                    {
                        sb.AppendLine("  角色属性为空（矩阵记 FAIL）");
                        continue;
                    }
                    bool appended = false;
                    // 候选路径 1：AppendGeometrySet(null 模板, 实体)
                    try
                    {
                        var append = geom.GetType().GetMethod("AppendGeometrySet");
                        var entities = Array.CreateInstance(typeof(DisplayableObject), 1);
                        entities.SetValue(a.Face, 0);
                        append.Invoke(geom, new object[] { null, entities });
                        appended = true;
                        sb.AppendLine("  路径1 AppendGeometrySet: ok");
                    }
                    catch (Exception ex1)
                    {
                        sb.AppendLine("  路径1 AppendGeometrySet 失败: " + Full(ex1));
                    }
                    // 候选路径 2：CreateGeometrySet → 其 Selection 的单参 Add（UI 选择的正规写入口候选）
                    if (!appended)
                    {
                        try
                        {
                            var cs = geom.GetType().GetMethod("CreateGeometrySet").Invoke(geom, null);
                            var sel = cs.GetType().GetProperty("Selection").GetValue(cs, null);
                            MethodInfo add = null;
                            foreach (var cand in sel.GetType().GetMethods())
                            {
                                if (cand.Name == "Add" && cand.GetParameters().Length == 1)
                                {
                                    add = cand;
                                    break;
                                }
                            }
                            if (add == null)
                            {
                                sb.AppendLine("  路径2 无单参 Add 方法（方法面: "
                                    + string.Join(",", System.Linq.Enumerable.Select(
                                        System.Linq.Enumerable.Where(sel.GetType().GetMethods(),
                                            m => m.Name == "Add"), m => m.ToString())));
                                continue;
                            }
                            add.Invoke(sel, new object[] { a.Face });
                            appended = true;
                            sb.AppendLine("  路径2 CreateGeometrySet+Selection.Add(单参): ok");
                        }
                        catch (Exception ex2)
                        {
                            sb.AppendLine("  路径2 失败: " + Full(ex2));
                        }
                    }
                    if (!appended)
                    {
                        sb.AppendLine("  挂接失败（两路径均不可用），矩阵记 FAIL");
                        continue;
                    }
                    // H4：Add 后 InitializeData 物化尝试（数据可能延迟落库）
                    try
                    {
                        geom.InitializeData(true);
                        sb.AppendLine("  InitializeData(true): ok");
                    }
                    catch (Exception exInit)
                    {
                        sb.AppendLine("  InitializeData(true) 失败: " + Full(exInit));
                    }
                    CommitAndDestroy(builder);
                    builder = null;
                    var list = new List<Face> { a.Face };
                    attached[a.Name] = list;
                    sb.AppendLine("  回读:");
                    ReadBackOpRoles(camSetup, op, sb);
                }
                catch (Exception ex)
                {
                    sb.AppendLine("  失败: " + Full(ex));
                }
                finally
                {
                    if (builder != null)
                    {
                        try
                        {
                            builder.GetType().GetMethod("Destroy").Invoke(builder, null);
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
            }
            // ---- 6. 组级实验：几何树全览 + WORKPIECE 几何组挂整个体（2.5D 自动几何的真正载体候选）----
            sb.AppendLine("== 组级 WORKPIECE 几何实验 ==");
            try
            {
                var gRoot = camSetup.GetRoot(CAMSetup.View.Geometry);
                sb.AppendLine("几何视图树: " + TreeDump(gRoot));
                // WORKPIECE 可能不在根直接子级（CreateGeometry 报「输入名已存在」）
                NCGroup wp = FindChildRec(gRoot, "WORKPIECE");
                sb.AppendLine("WORKPIECE 定位: " + (wp == null ? "(未找到——几何树见上)" : wp.Name));
                if (wp == null)
                {
                    try
                    {
                        wp = camSetup.CAMGroupCollection.CreateGeometry(gRoot,
                            NxTemplateKeys.SetupFamily, "WORKPIECE",
                            NCGroupCollection.UseDefaultName.False, "WORKPIECE");
                        sb.AppendLine("WORKPIECE 组已建: " + (wp == null ? "(null)" : wp.Name));
                    }
                    catch (Exception exWp)
                    {
                        sb.AppendLine("WORKPIECE 组创建失败（subtype 猜测): " + Full(exWp));
                    }
                }
                if (wp != null && bodies.Length > 0)
                {
                    var attachedRoles = new List<string>();
                    object mgb = null;
                    try
                    {
                        mgb = camSetup.CAMGroupCollection.CreateMillGeomBuilder(wp);
                        sb.AppendLine("MillGeomBuilder: " + mgb.GetType().Name);
                        // 全属性面（找 Blank/Stock 等真实角色名与其类型）
                        sb.AppendLine("  属性面: " + string.Join(",",
                            new List<string>(System.Linq.Enumerable.Select(
                                mgb.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance),
                                pr => pr.Name + ":" + pr.PropertyType.Name))));
                        foreach (PropertyInfo p in mgb.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                        {
                            if (p.PropertyType != typeof(NXOpen.CAM.Geometry))
                            {
                                continue;
                            }
                            var geom = p.GetValue(mgb, null) as NXOpen.CAM.Geometry;
                            if (geom == null)
                            {
                                sb.AppendLine("  role " + p.Name + ": (null)");
                                continue;
                            }
                            try
                            {
                                var cs = geom.GetType().GetMethod("CreateGeometrySet").Invoke(geom, null);
                                var sel = cs.GetType().GetProperty("Selection").GetValue(cs, null);
                                MethodInfo add = null;
                                foreach (var cand in sel.GetType().GetMethods())
                                {
                                    if (cand.Name == "Add" && cand.GetParameters().Length == 1)
                                    {
                                        add = cand;
                                        break;
                                    }
                                }
                                add.Invoke(sel, new object[] { bodies[0] });
                                geom.InitializeData(true);
                                attachedRoles.Add(p.Name);
                                sb.AppendLine("  role " + p.Name + ": Selection.Add(body) ok");
                            }
                            catch (Exception exRole)
                            {
                                sb.AppendLine("  role " + p.Name + " 挂接失败: " + Full(exRole));
                            }
                        }
                        CommitAndDestroy(mgb);
                        mgb = null;
                        sb.AppendLine("  Commit: ok");
                        // Commit 后重开 builder 回读（验证真落库）
                        foreach (var role in attachedRoles)
                        {
                            try
                            {
                                var b2 = camSetup.CAMGroupCollection.CreateMillGeomBuilder(wp);
                                try
                                {
                                    var geom2 = b2.GetType().GetProperty(role).GetValue(b2, null) as NXOpen.CAM.Geometry;
                                    if (geom2 == null)
                                    {
                                        sb.AppendLine("  回读 " + role + ": (null)");
                                        continue;
                                    }
                                    var sets = geom2.GeometryList.GetContents();
                                    int items = 0;
                                    var kinds = new Dictionary<string, int>();
                                    if (sets != null)
                                    {
                                        foreach (var set in sets)
                                        {
                                            var it = set.GetItems();
                                            if (it == null)
                                            {
                                                continue;
                                            }
                                            items += it.Length;
                                            foreach (var item in it)
                                            {
                                                var t = item.GetType().Name;
                                                kinds[t] = kinds.ContainsKey(t) ? kinds[t] + 1 : 1;
                                            }
                                        }
                                    }
                                    sb.AppendLine("  回读 " + role + ": sets=" + (sets == null ? 0 : sets.Length)
                                        + " items=" + items + " kinds=" + DictSum(kinds));
                                }
                                finally
                                {
                                    b2.GetType().GetMethod("Destroy").Invoke(b2, null);
                                }
                            }
                            catch (Exception exBack)
                            {
                                sb.AppendLine("  回读 " + role + " 失败: " + Full(exBack));
                            }
                        }
                    }
                    catch (Exception exMg)
                    {
                        sb.AppendLine("MillGeomBuilder 实验失败: " + Full(exMg));
                    }
                    finally
                    {
                        if (mgb != null)
                        {
                            try
                            {
                                mgb.GetType().GetMethod("Destroy").Invoke(mgb, null);
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }
                }
            }
            catch (Exception exOuter)
            {
                sb.AppendLine("组级实验整体失败: " + Full(exOuter));
            }
            return sb.ToString();
        }

        private sealed class Attach
        {
            public string Name;
            public string Role;
            public Face Face;
        }

        /// <summary>组装导出快照：基快照 + 补 Faces 表与 op.GeometryTags（挂接成功的面）。</summary>
        public static CamSetupSnapshot BuildSnapshotWithFaces(
            CAMSetup camSetup, string partName, string inputRef,
            DiagnosticsCollector diag, Dictionary<string, List<Face>> attached)
        {
            var snapshot = NxSnapshotReader.Read(camSetup, partName, inputRef, diag);
            var opsByName = new Dictionary<string, OperationSnapshot>();
            CollectOps(snapshot.ProgramRoot, opsByName);
            foreach (var pair in attached)
            {
                if (!opsByName.TryGetValue(pair.Key, out var opSnap))
                {
                    continue;
                }
                foreach (var face in pair.Value)
                {
                    var fs = FaceSnapshotOf(face);
                    if (fs == null)
                    {
                        continue;
                    }
                    if (!snapshot.Faces.Exists(f => f.Tag.Equals(fs.Tag)))
                    {
                        snapshot.Faces.Add(fs);
                    }
                    if (!opSnap.GeometryTags.Contains(fs.Tag))
                    {
                        opSnap.GeometryTags.Add(fs.Tag);
                    }
                }
            }
            return snapshot;
        }

        private static void CollectOps(GroupSnapshot group, Dictionary<string, OperationSnapshot> target)
        {
            if (group == null)
            {
                return;
            }
            foreach (var op in group.Operations)
            {
                target[op.Name] = op;
            }
            foreach (var child in group.Children)
            {
                CollectOps(child, target);
            }
        }

        private static FaceSnapshot FaceSnapshotOf(Face face)
        {
            try
            {
                var uf = UFSession.GetUFSession();
                int type;
                var point = new double[3];
                var dir = new double[3];
                var box = new double[6];
                double r0, r1;
                int normDir;
                uf.Modl.AskFaceData(face.Tag, out type, point, dir, box, out r0, out r1, out normDir);
                return new FaceSnapshot
                {
                    Tag = face.Tag,
                    // 质心/面积精确 API 不可得（.NET UF 无 ask_face_area/normals——nxopen-research
                    // §2.3 过时）；AskFaceData.point 为面上固定点，暂作稳定锚点，精确口径 M4a 定。
                    Centroid = new[] { point[0], point[1], point[2] },
                    Area = 0,
                    FaceType = "TYPE_" + type,
                    Normal = dir,
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void ReadBackOpRoles(CAMSetup camSetup, NXOpen.CAM.Operation op, StringBuilder sb)
        {
            try
            {
                var b = camSetup.CAMOperationCollection.CreateBuilder(op);
                try
                {
                    foreach (PropertyInfo p in b.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (p.PropertyType != typeof(NXOpen.CAM.Geometry))
                        {
                            continue;
                        }
                        var geom = p.GetValue(b, null) as NXOpen.CAM.Geometry;
                        if (geom == null || geom.Tag == Tag.Null)
                        {
                            continue;
                        }
                        int n = 0;
                        int setCount = 0;
                        var sets = geom.GeometryList.GetContents();
                        if (sets != null)
                        {
                            setCount = sets.Length;
                            foreach (var set in sets)
                            {
                                var items = set.GetItems();
                                if (items != null)
                                {
                                    n += items.Length;
                                }
                            }
                        }
                        sb.AppendLine("    " + p.Name + ": sets=" + setCount + " items=" + n);
                    }
                }
                finally
                {
                    b.GetType().GetMethod("Destroy").Invoke(b, null);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("  回读失败: " + Short(ex));
            }
        }

        private static NCGroup FindChild(NCGroup parent, string name)
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

        private static NCGroup FindChildRec(NCGroup root, string name)
        {
            if (root == null)
            {
                return null;
            }
            if (root.Name == name)
            {
                return root;
            }
            foreach (CAMObject member in root.GetMembers())
            {
                if (member is NCGroup child)
                {
                    var found = FindChildRec(child, name);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }
            return null;
        }

        private static string TreeDump(NCGroup root)
        {
            var sb = new StringBuilder();
            WalkDump(root, 0, sb);
            return sb.ToString();
        }

        private static void WalkDump(NCGroup group, int depth, StringBuilder sb)
        {
            if (group == null)
            {
                return;
            }
            sb.Append('\n').Append(new string(' ', depth * 2)).Append(group.Name);
            foreach (CAMObject member in group.GetMembers())
            {
                if (member is NCGroup child)
                {
                    WalkDump(child, depth + 1, sb);
                }
            }
        }

        private static void CommitAndDestroy(object builder)
        {
            builder.GetType().GetMethod("Commit").Invoke(builder, null);
            builder.GetType().GetMethod("Destroy").Invoke(builder, null);
        }

        private static string Short(Exception ex)
        {
            var msg = ex.Message;
            return msg != null && msg.Length > 200 ? msg.Substring(0, 200) : msg;
        }

        private static string DictSum(Dictionary<string, int> d)
        {
            var parts = new List<string>();
            foreach (var kv in d)
            {
                parts.Add(kv.Key + "=" + kv.Value);
            }
            return string.Join(" ", parts);
        }

        /// <summary>完整异常链（反射 TargetInvocationException 会吞内层 NX 错误，须逐层展开）。</summary>
        private static string Full(Exception ex)
        {
            var sb = new StringBuilder();
            var cur = ex;
            while (cur != null)
            {
                if (sb.Length > 0)
                {
                    sb.Append(" <- ");
                }
                sb.Append(cur.GetType().Name).Append(": ").Append(Short(cur));
                cur = cur.InnerException;
            }
            return sb.Length > 600 ? sb.ToString().Substring(0, 600) : sb.ToString();
        }
    }
}
