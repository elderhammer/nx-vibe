using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;

namespace Autocam.Nx.Adapter.Export
{
    /// <summary>
    /// M4 子步0 探针 v3：工序↔关联几何读取链路实证。
    /// v2 结论（模板件）：全部 Builder 有 GeometryCiBuilder 通用角色（v1 漏网主因）；
    /// 平面族另有 Boundary/BoundaryPlanarMill 角色；2.5D 体积类五角色 set 空但 op 带
    /// InsertFeature/RemoveFeature（特征驱动）；组级 CreateMillGeomBuilder 对 MCS 组抛
    /// Orient 强转错。确定性两遍一致 ✓。
    /// v3 扩：① CiBuilder/Boundary/Feature 容器递归深钻（类×深度去重缓存）；
    /// ② CAM.Geometry 空集时 InitializeData(true) 重读；③ 几何组逐组遍历（Orient 组豁免）；
    /// ④ 第二零件分步诊断打开（OpenDisplay/SetWork/CAMSetup 逐步定位 NRE）。
    /// </summary>
    public static class NxGeometryProbe
    {
        private static readonly HashSet<string> Drilled = new HashSet<string>();

        public static string Probe(CAMSetup camSetup)
        {
            Drilled.Clear();
            var sb = new StringBuilder();
            sb.AppendLine("== 零件: " + Session.GetSession().Parts.Work.FullPath);
            var facesToProbe = new List<NXOpen.Face>();

            // ---- Pass A：按 Builder 类目录化几何承载属性（首次出现该类时打一次）----
            sb.AppendLine("-- Pass A: 按 Builder 类目录化几何承载属性 --");
            var catalogued = new HashSet<string>();
            foreach (NXOpen.CAM.Operation op in camSetup.CAMOperationCollection.ToArray())
            {
                OperationBuilder builder = null;
                try
                {
                    builder = camSetup.CAMOperationCollection.CreateBuilder(op);
                    var bt = builder.GetType().Name;
                    if (!catalogued.Add(bt))
                    {
                        continue;
                    }
                    sb.AppendLine("== builder 类 " + bt + "（代表工序 " + op.Name + "）");
                    foreach (PropertyInfo p in builder.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (!IsGeometryish(p))
                        {
                            continue;
                        }
                        sb.AppendLine("  prop " + p.Name + " : " + p.PropertyType.Name);
                        object val;
                        try
                        {
                            val = p.GetValue(builder, null);
                        }
                        catch (Exception ex)
                        {
                            sb.AppendLine("    READ_FAIL " + Short(ex));
                            continue;
                        }
                        if (val == null)
                        {
                            sb.AppendLine("    (null)");
                            continue;
                        }
                        DescribeValue(sb, "    ", val, facesToProbe, 0, "top");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine("== builder 类目录化失败: " + Short(ex));
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
                        }
                    }
                }
            }

            // ---- Pass B：逐 op NXOpen.CAM.Geometry 角色载荷（含 InitializeData 重读）----
            sb.AppendLine("-- Pass B: 逐 op 几何角色载荷（类型=CAM.Geometry） --");
            var roleNames = new Dictionary<string, int>();
            foreach (NXOpen.CAM.Operation op in camSetup.CAMOperationCollection.ToArray())
            {
                var opLine = new StringBuilder();
                opLine.Append("op[").Append(op.Name).Append("] ");
                OperationBuilder builder = null;
                try
                {
                    builder = camSetup.CAMOperationCollection.CreateBuilder(op);
                    opLine.Append("builder=").Append(builder.GetType().Name);
                    var seen = new HashSet<NXOpen.Tag>();
                    foreach (PropertyInfo p in builder.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (p.PropertyType != typeof(NXOpen.CAM.Geometry))
                        {
                            continue;
                        }
                        NXOpen.CAM.Geometry geom;
                        try
                        {
                            geom = p.GetValue(builder, null) as NXOpen.CAM.Geometry;
                        }
                        catch (Exception)
                        {
                            continue;
                        }
                        if (geom == null || geom.Tag == Tag.Null || !seen.Add(geom.Tag))
                        {
                            continue;
                        }
                        var load = ReadSets(geom, facesToProbe);
                        if (load.Items == 0 && !load.Reloaded)
                        {
                            // 空集 → InitializeData 重载一次再读（v3 新增）
                            try
                            {
                                geom.InitializeData(true);
                                var retry = ReadSets(geom, facesToProbe);
                                load = new SetsSummary(load.Sets, retry.Items, true);
                            }
                            catch (Exception ex)
                            {
                                opLine.Append(" | ").Append(p.Name).Append("=INIT_FAIL:").Append(Short(ex));
                                continue;
                            }
                        }
                        roleNames[p.Name] = roleNames.ContainsKey(p.Name) ? roleNames[p.Name] + 1 : 1;
                        opLine.Append(" | ").Append(p.Name).Append(": sets=").Append(load.Sets)
                            .Append(" items=").Append(load.Items).Append(load.Reloaded ? "(重载后)" : "");
                    }
                }
                catch (Exception ex)
                {
                    opLine.Append(" | BUILDER_FAIL:").Append(Short(ex));
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
                        }
                    }
                }
                sb.AppendLine(opLine.ToString());
            }
            sb.AppendLine("== 角色名频次: " + DictSummary(roleNames));

            // ---- 组级几何：逐几何组（Orient 组豁免 InvalidCast）----
            sb.AppendLine("-- 组级几何（逐几何组） --");
            ProbeGroupRoles(camSetup, sb, facesToProbe);

            // ---- P3：面属性 API（随命中的面）----
            sb.AppendLine("== 面属性 API ==");
            ProbeFaceAttributes(sb, facesToProbe);

            // ---- P4：确定性指纹 ----
            sb.AppendLine("== 确定性 ==");
            var fp1 = Fingerprint(camSetup);
            var fp2 = Fingerprint(camSetup);
            sb.AppendLine("两遍枚举指纹一致: " + (fp1 == fp2));
            if (fp1 != fp2)
            {
                sb.AppendLine("fp1: " + fp1);
                sb.AppendLine("fp2: " + fp2);
            }
            return sb.ToString();
        }

        private sealed class SetsSummary
        {
            public SetsSummary(int sets, int items, bool reloaded)
            {
                Sets = sets;
                Items = items;
                Reloaded = reloaded;
            }
            public int Sets;
            public int Items;
            public bool Reloaded;
        }

        private static SetsSummary ReadSets(NXOpen.CAM.Geometry geom, List<NXOpen.Face> facesToProbe)
        {
            var sets = geom.GeometryList.GetContents();
            if (sets == null)
            {
                return new SetsSummary(0, 0, false);
            }
            int items = 0;
            foreach (var set in sets)
            {
                var setItems = set.GetItems();
                if (setItems == null)
                {
                    continue;
                }
                items += setItems.Length;
                foreach (var item in setItems)
                {
                    CollectItem(item, facesToProbe);
                }
            }
            return new SetsSummary(sets.Length, items, false);
        }

        // ---- 几何承载属性识别 ----

        private static bool IsGeometryish(PropertyInfo p)
        {
            return IsGeometryishName(p.Name + p.PropertyType.Name);
        }

        /// <summary>值的形状自描述；容器类（Ci/Boundary/Feature）递归深钻，类×深度去重防爆炸。</summary>
        private static void DescribeValue(StringBuilder sb, string indent, object val, List<NXOpen.Face> facesToProbe, int depth, string context)
        {
            if (val is NXOpen.CAM.Geometry geom)
            {
                var desc = new StringBuilder();
                desc.Append(indent).Append("CAM.Geometry");
                try
                {
                    var load = ReadSets(geom, facesToProbe);
                    desc.Append(" sets=").Append(load.Sets).Append(" items=").Append(load.Items);
                    if (load.Items == 0)
                    {
                        try
                        {
                            geom.InitializeData(true);
                            var retry = ReadSets(geom, facesToProbe);
                            if (retry.Items > 0)
                            {
                                desc.Append(" →重载后 items=").Append(retry.Items);
                            }
                        }
                        catch (Exception ex)
                        {
                            desc.Append(" INIT_FAIL:").Append(Short(ex));
                        }
                    }
                }
                catch (Exception ex)
                {
                    desc.Append(" LIST_FAIL:").Append(Short(ex));
                }
                sb.AppendLine(desc.ToString());
                return;
            }
            var t = val.GetType();
            var tn = t.Name;
            bool drillable = tn.IndexOf("Ci", StringComparison.Ordinal) >= 0
                || tn.IndexOf("Boundary", StringComparison.OrdinalIgnoreCase) >= 0
                || tn.IndexOf("Feature", StringComparison.OrdinalIgnoreCase) >= 0
                || tn.EndsWith("Set", StringComparison.Ordinal)   // BoundarySet 等集合（GeometrySet 不出现在属性面）
                || tn.EndsWith("Builder", StringComparison.Ordinal);
            if (drillable && depth < 4)
            {
                var key = context + "#" + depth + "#" + tn;
                if (!Drilled.Add(key))
                {
                    sb.AppendLine(indent + tn + "（已钻，跳过）");
                    return;
                }
                sb.AppendLine(indent + tn + " 深钻:");
                bool found = false;
                foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!IsGeometryish(p))
                    {
                        continue;
                    }
                    object inner;
                    try
                    {
                        inner = p.GetValue(val, null);
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine(indent + "  " + p.Name + " READ_FAIL " + Short(ex));
                        continue;
                    }
                    if (inner == null)
                    {
                        continue;
                    }
                    found = true;
                    sb.AppendLine(indent + "  " + p.Name + " : " + p.PropertyType.Name);
                    DescribeValue(sb, indent + "    ", inner, facesToProbe, depth + 1, tn);
                }
                // 零参 Get*/Ask* 几何方法尝试（Ci 选择器读侧多为方法面）
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.GetParameters().Length != 0)
                    {
                        continue;
                    }
                    if (!(m.Name.StartsWith("Get", StringComparison.Ordinal) || m.Name.StartsWith("Ask", StringComparison.Ordinal)))
                    {
                        continue;
                    }
                    if (!IsGeometryishName(m.Name + m.ReturnType.Name))
                    {
                        continue;
                    }
                    try
                    {
                        var r = m.Invoke(val, null);
                        sb.AppendLine(indent + "  method " + m.Name + " : " + m.ReturnType.Name
                            + " → " + (r == null ? "(null)" : Shape(r, facesToProbe)));
                        // 集合返回值：元素递归钻取（白名单 + ≤3 个防爆炸）
                        if (r is System.Collections.IEnumerable coll && !(r is string))
                        {
                            int shown = 0;
                            foreach (var e in coll)
                            {
                                if (e == null)
                                {
                                    continue;
                                }
                                var et = e.GetType().Name;
                                if (et.IndexOf("Boundary", StringComparison.OrdinalIgnoreCase) < 0
                                    && et.IndexOf("Geometr", StringComparison.OrdinalIgnoreCase) < 0
                                    && et.IndexOf("Curve", StringComparison.OrdinalIgnoreCase) < 0
                                    && et.IndexOf("Edge", StringComparison.OrdinalIgnoreCase) < 0
                                    && et.IndexOf("Face", StringComparison.OrdinalIgnoreCase) < 0
                                    && !et.EndsWith("Set", StringComparison.Ordinal))
                                {
                                    continue;
                                }
                                var tagPart = e is NXObject eo ? " tag=" + eo.Tag.ToString("X8") : "";
                                sb.AppendLine(indent + "    element " + et + tagPart);
                                if (e is NXOpen.Face ef)
                                {
                                    CollectItem(ef, facesToProbe);
                                }
                                DescribeValue(sb, indent + "      ", e, facesToProbe, depth + 1, tn);
                                shown++;
                                if (shown >= 3)
                                {
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine(indent + "  method " + m.Name + " INVOKE_FAIL " + Short(ex));
                    }
                }
                if (!found)
                {
                    sb.AppendLine(indent + "  (无几何承载属性)");
                }
                return;
            }
            if (val is System.Collections.IEnumerable list)
            {
                var count = 0;
                var kinds = new Dictionary<string, int>();
                foreach (var e in list)
                {
                    count++;
                    var k = e == null ? "(null)" : e.GetType().Name;
                    kinds[k] = kinds.ContainsKey(k) ? kinds[k] + 1 : 1;
                }
                sb.AppendLine(indent + "list[" + count + "] kinds=" + DictSummary(kinds));
                return;
            }
            if (val is NXObject nx)
            {
                sb.AppendLine(indent + "NXObject " + nx.GetType().Name + " tag=" + nx.Tag.ToString("X8"));
                return;
            }
            sb.AppendLine(indent + val.GetType().FullName);
        }

        private static bool IsGeometryishName(string n)
        {
            return n.IndexOf("Geometr", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Selection", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Boundary", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Feature", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Floor", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Wall", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Face", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Drive", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>方法返回值形状（单值摘要，不深钻防爆炸）。</summary>
        private static string Shape(object r, List<NXOpen.Face> facesToProbe)
        {
            if (r is NXOpen.CAM.Geometry geom)
            {
                try
                {
                    var load = ReadSets(geom, facesToProbe);
                    return "CAM.Geometry sets=" + load.Sets + " items=" + load.Items;
                }
                catch (Exception)
                {
                    return "CAM.Geometry (读失败)";
                }
            }
            if (r is System.Collections.IEnumerable list)
            {
                var count = 0;
                foreach (var e in list)
                {
                    count++;
                }
                return "list[" + count + "]";
            }
            if (r is NXObject nx)
            {
                return "NXObject " + nx.GetType().Name;
            }
            return r.GetType().Name;
        }

        private static void CollectItem(object item, List<NXOpen.Face> facesToProbe)
        {
            if (item is NXOpen.Face face && facesToProbe.Count < 6 && !facesToProbe.Contains(face))
            {
                facesToProbe.Add(face);
            }
        }

        private static void ProbeGroupRoles(CAMSetup camSetup, StringBuilder sb, List<NXOpen.Face> facesToProbe)
        {
            var root = camSetup.GetRoot(CAMSetup.View.Geometry);
            if (root == null)
            {
                sb.AppendLine("(无几何视图根)");
                return;
            }
            foreach (CAMObject member in root.GetMembers())
            {
                var group = member as NCGroup;
                if (group == null)
                {
                    continue;
                }
                sb.AppendLine("几何组: " + group.Name);
                object builder = null;
                try
                {
                    builder = camSetup.CAMGroupCollection.CreateMillGeomBuilder(group);
                    foreach (PropertyInfo p in builder.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (!IsGeometryish(p))
                        {
                            continue;
                        }
                        object val;
                        try
                        {
                            val = p.GetValue(builder, null);
                        }
                        catch (Exception ex)
                        {
                            sb.AppendLine("  " + p.Name + " READ_FAIL " + Short(ex));
                            continue;
                        }
                        if (val == null)
                        {
                            continue;
                        }
                        DescribeValue(sb, "  ", val, facesToProbe, 0, "group");
                    }
                }
                catch (InvalidCastException ex)
                {
                    sb.AppendLine("  (MCS/Orient 型组，MillGeom 工厂不适用: " + Short(ex) + ")");
                }
                catch (Exception ex)
                {
                    sb.AppendLine("  MillGeomBuilder 不可用: " + Short(ex));
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
        }

        private static void ProbeFaceAttributes(StringBuilder sb, List<NXOpen.Face> faces)
        {
            if (faces.Count == 0)
            {
                sb.AppendLine("(无工序/组关联面可探)");
                return;
            }
            UFSession uf;
            try
            {
                uf = UFSession.GetUFSession();
            }
            catch (Exception ex)
            {
                sb.AppendLine("UFSession 不可用: " + Short(ex));
                return;
            }
            foreach (var face in faces)
            {
                var line = new StringBuilder();
                line.Append("face tag=").Append(face.Tag).Append(" ");
                var point = new double[3];
                var dir = new double[3];
                var box = new double[6];
                try
                {
                    int typeCode;
                    double radii0, radii1;
                    int normDir;
                    uf.Modl.AskFaceData(face.Tag, out typeCode, point, dir, box, out radii0, out radii1, out normDir);
                    line.Append("type=").Append(typeCode).Append(" point=[").Append(Vec(point))
                        .Append("] dir=[").Append(Vec(dir)).Append("] normDir=").Append(normDir)
                        .Append(" radii=[").Append(radii0.ToString("F6")).Append(",").Append(radii1.ToString("F6")).Append("]");
                }
                catch (Exception ex)
                {
                    line.Append("AskFaceData_FAIL:").Append(Short(ex));
                }
                try
                {
                    var param = new double[2];
                    var p = new double[3];
                    var u1 = new double[3];
                    var v1 = new double[3];
                    var u2 = new double[3];
                    var v2 = new double[3];
                    var un = new double[3];
                    var radii = new double[2];
                    uf.Modl.AskFaceProps(face.Tag, param, p, u1, v1, u2, v2, un, radii);
                    line.Append(" | props@(0,0): point=[").Append(Vec(p)).Append("] unit_norm=[")
                        .Append(Vec(un)).Append("]");
                }
                catch (Exception ex)
                {
                    line.Append(" | AskFaceProps_FAIL:").Append(Short(ex));
                }
                sb.AppendLine(line.ToString());
            }
            sb.AppendLine("注：面积/质心无托管无参命中 → M4a 面积路线待定（AskFaceData point/面内点可作稳定锚点候选）。");
        }

        private static string Fingerprint(CAMSetup camSetup)
        {
            var sb = new StringBuilder();
            foreach (NXOpen.CAM.Operation op in camSetup.CAMOperationCollection.ToArray())
            {
                sb.Append("|").Append(op.Name);
                OperationBuilder builder = null;
                try
                {
                    builder = camSetup.CAMOperationCollection.CreateBuilder(op);
                    var seen = new HashSet<NXOpen.Tag>();
                    foreach (PropertyInfo p in builder.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (p.PropertyType != typeof(NXOpen.CAM.Geometry))
                        {
                            continue;
                        }
                        try
                        {
                            var geom = p.GetValue(builder, null) as NXOpen.CAM.Geometry;
                            if (geom == null || geom.Tag == Tag.Null || !seen.Add(geom.Tag))
                            {
                                continue;
                            }
                            var sets = geom.GeometryList.GetContents();
                            for (int s = 0; sets != null && s < sets.Length; s++)
                            {
                                var items = sets[s].GetItems();
                                for (int i = 0; items != null && i < items.Length; i++)
                                {
                                    sb.Append("~").Append(p.Name).Append("#").Append(s).Append("#")
                                        .Append(items[i].GetType().Name).Append("#").Append(items[i].Tag.ToString("X8"));
                                }
                            }
                        }
                        catch (Exception)
                        {
                        }
                    }
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
                        }
                    }
                }
            }
            return sb.ToString();
        }

        private static string Short(Exception ex)
        {
            var msg = ex.Message;
            return msg != null && msg.Length > 160 ? msg.Substring(0, 160) : msg;
        }

        private static string Vec(double[] v)
        {
            if (v == null)
            {
                return "(null)";
            }
            var parts = new List<string>();
            for (int i = 0; i < v.Length; i++)
            {
                parts.Add(v[i].ToString("F6"));
            }
            return string.Join(",", parts);
        }

        private static string DictSummary(Dictionary<string, int> d)
        {
            var parts = new List<string>();
            foreach (var kv in d)
            {
                parts.Add(kv.Key + "=" + kv.Value);
            }
            return string.Join(" ", parts);
        }
    }
}
