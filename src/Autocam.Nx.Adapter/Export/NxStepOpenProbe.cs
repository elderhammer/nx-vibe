using System;
using System.IO;
using System.Reflection;
using System.Text;
using NXOpen;
using NXOpen.CAM;
using Path = System.IO.Path;

namespace Autocam.Nx.Adapter.Export
{
    /// <summary>
    /// M4c S0 STEP 打开探针（运行时实证；静态反射面先证于 C:\nx-vibe-journal-out\m4c_reflect*.ps1，
    /// 结论链入核对清单 M4c 节）：Session.DexManager → CreateStep203/214/242Importer →
    /// InputFile + ImportTo(WorkPart|NewPart) → Commit。Builder 语义（Commit/Destroy）。
    ///
    /// Q1 STEP 打开路径：直开 OpenDisplay（GUI File→Open 翻译语义）vs DexManager 翻译器
    /// Q2 打开产物挂 CAMSetup 可行性（重建载体两径：翻译 NewPart 后挂 / 已挂 CAMSetup 宿主件 WorkPart 导入）
    /// Q3 落位：实体数 + WCS 原点（vs 对照件）
    /// Q4 模式差异：批处理（UGII_BATCH_MODE=1 run_journal）vs GUI File→Execute——报告头标注，两次运行对照判读
    /// S1 自动化：fixture（m4_gt_face.stp）缺失时经 DexManager.CreateStepCreator 从对照件自动导出
    /// （ExportAs=Ap214；输出文件名=零件名，OutputFile 仅定目录），失败不假成功、仍留 GUI 手动兜底指引。
    /// </summary>
    public static class NxStepOpenProbe
    {
        private const string CamTemplate = "mill_planar";

        public static string Run(Session session, string controlPath, string stepPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("== M4c S0 STEP 打开探针 ==");
            sb.AppendLine("模式: " + (Environment.GetEnvironmentVariable("UGII_BATCH_MODE") == "1" ? "批处理(UGII_BATCH_MODE=1)" : "GUI/交互会话"));
            sb.AppendLine("control=" + controlPath);
            sb.AppendLine("step=" + stepPath);
            sb.AppendLine("step 存在=" + File.Exists(stepPath));
            try
            {
                sb.AppendLine("会话已开零件数=" + session.Parts.ToArray().Length);
            }
            catch (Exception ex)
            {
                sb.AppendLine("会话零件枚举失败: " + Short(ex));
            }

            // ---- 对照件（手编 CAM 件基线：WCS 落位 + CAMSetup 参照）----
            Part controlPart = null;
            if (!string.IsNullOrEmpty(controlPath) && File.Exists(controlPath))
            {
                try
                {
                    controlPart = OpenOrFind(session, controlPath, sb);
                    if (controlPart != null)
                    {
                        sb.AppendLine("-- 对照件 --");
                        DumpPartInfo(sb, controlPart);
                    }
                    else
                    {
                        sb.AppendLine("对照件不可用（跳过）");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine("对照件处理失败: " + Short(ex));
                }
            }

            // ---- S1 自动化：fixture 缺失时自动导出（静态实证：DexManager.CreateStepCreator →
            // ExportFrom/ExportAs/OutputFile → Commit；实测 OutputFile 仅定目录、输出文件名=零件名，
            // 故 stepPath 须用 <对照件零件名>.stp；失败如实记录，走 GUI 手动兜底）----
            var stepMissing = string.IsNullOrEmpty(stepPath) || !File.Exists(stepPath);
            if (stepMissing)
            {
                sb.AppendLine("-- S1 fixture 自举（自动导出） --");
                if (controlPart != null)
                {
                    try
                    {
                        var creator = session.DexManager.CreateStepCreator();
                        creator.ExportFrom = NXOpen.StepCreator.ExportFromOption.DisplayPart;
                        creator.ExportAs = NXOpen.StepCreator.ExportAsOption.Ap214;
                        creator.OutputFile = stepPath;
                        creator.Commit();
                        creator.Destroy();
                        sb.AppendLine("自动导出: " + (File.Exists(stepPath) ? "ok → " + stepPath : "未落盘（无异常但文件不存在）"));
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("自动导出失败: " + Short(ex));
                    }
                    stepMissing = !File.Exists(stepPath);
                }
                else
                {
                    sb.AppendLine("无对照件可导出（跳过）");
                }
                if (stepMissing)
                {
                    sb.AppendLine("STEP fixture 仍缺失 → 本跑仅验对照路径。手动兜底：NX GUI 打开 parts\\m4_gt_face.prt "
                        + "→ File→Export→STEP（输出名=零件名 m4_gt_face.stp）→ 再复跑本探针。");
                    return sb.ToString();
                }
            }

            sb.AppendLine("-- STEP 主题: " + Path.GetFileName(stepPath) + " --");

            // Q1-① 直开（GUI File→Open 对 STEP 走隐式翻译；批处理无显示预期失败——失败即结论，不静默）
            try
            {
                PartLoadStatus ls;
                var direct = session.Parts.OpenDisplay(stepPath, out ls);
                session.Parts.SetWork(direct);
                sb.AppendLine("① OpenDisplay 直开成功（隐式翻译生效）");
                DumpPartInfo(sb, direct);
                TryAttachCamSetup(sb, direct);
            }
            catch (Exception ex)
            {
                sb.AppendLine("① OpenDisplay 直开失败: " + Short(ex));
            }

            // Q1-② DexManager 翻译器（静态实证链；逐个 schema 尝试——203/214/242 全记录）
            foreach (var factoryName in new[] { "CreateStep203Importer", "CreateStep214Importer", "CreateStep242Importer" })
            {
                sb.AppendLine("-- 翻译器 " + factoryName + " (NewPart) --");
                TryDexImport(session, sb, factoryName, stepPath, importToWorkPart: false);
            }

            // Q2-路径B：已挂 CAMSetup 宿主件 + WorkPart 导入（把几何倒进 CAM 工件的重建架构备选）
            sb.AppendLine("-- Q2 路径B: CAMSetup 宿主件 + WorkPart 导入 --");
            CAMSetup host = null;
            try
            {
                host = SessionBootstrap.NewPartWithCamSetup(session, "m4c_host_" + DateTime.Now.ToString("HHmmss"));
                sb.AppendLine("宿主件已建（含 CAMSetup）: ok");
            }
            catch (Exception ex)
            {
                sb.AppendLine("宿主件创建失败: " + Short(ex));
            }
            if (host != null)
            {
                var before = CountBodies(session.Parts.Work);
                TryDexImport(session, sb, "CreateStep203Importer", stepPath, importToWorkPart: true);
                var after = CountBodies(session.Parts.Work);
                sb.AppendLine("宿主件 WorkPart 导入后 bodies: " + before + " → " + after);
                sb.AppendLine("宿主件 CAMSetup 仍在: " + (session.Parts.Work.CAMSetup != null));
            }

            return sb.ToString();
        }

        /// <summary>Q1/Q2 通用：DexManager 工厂 → 设参 → Commit → 定位产物 part → dump → 路径A 挂 CAMSetup。</summary>
        private static void TryDexImport(Session session, StringBuilder sb, string factoryName, string stepPath,
            bool importToWorkPart)
        {
            object imp = null;
            try
            {
                imp = session.DexManager.GetType().GetMethod(factoryName).Invoke(session.DexManager, null);
                if (imp == null)
                {
                    sb.AppendLine("  " + factoryName + " 返回 null（翻译器不可用）");
                    return;
                }
                var t = imp.GetType();
                t.GetProperty("InputFile").SetValue(imp, stepPath, null);
                var importTo = t.GetProperty("ImportTo");
                if (importTo != null)
                {
                    var enumType = importTo.PropertyType;
                    importTo.SetValue(imp, Enum.Parse(enumType, importToWorkPart ? "WorkPart" : "NewPart"), null);
                    sb.AppendLine("  ImportTo=" + (importToWorkPart ? "WorkPart" : "NewPart"));
                }
                else
                {
                    sb.AppendLine("  无 ImportTo 属性（缺省路径）");
                }
                // 疑点排查：BaseImporter.SetMode 缺省可能为 Teamcenter——无 TC 会话下导入失败；
                // 显式 NativeFileSystem（Mode 枚举声明于 BaseImporter，编译期直调）
                try
                {
                    if (imp is NXOpen.BaseImporter bi)
                    {
                        bi.SetMode(NXOpen.BaseImporter.Mode.NativeFileSystem);
                        sb.AppendLine("  SetMode=NativeFileSystem");
                    }
                    else
                    {
                        sb.AppendLine("  产物非 BaseImporter（" + imp.GetType().Name + "）");
                    }
                }
                catch (Exception modeEx)
                {
                    sb.AppendLine("  SetMode 失败（继续）: " + Short(modeEx));
                }
                sb.AppendLine("  已开会话零件数=" + session.Parts.ToArray().Length + " → Commit …");
                var committed = (NXObject)t.GetMethod("Commit").Invoke(imp, null);
                if (committed == null)
                {
                    sb.AppendLine("  Commit 返回 null（翻译器静默失败——疑许可/内部状态；OpenDisplay 主径不受影响）");
                    return;
                }
                sb.AppendLine("  Commit 成功 → " + committed.GetType().Name + " tag=" + committed.Tag.ToString("X8"));
                var part = committed as Part;
                if (part == null)
                {
                    // Commit 产物非显示 Part（批处理无显示态候选）；按 step 文件名 stem 在会话里找
                    var stem = Path.GetFileNameWithoutExtension(stepPath);
                    foreach (Part p in session.Parts)
                    {
                        if (p.FullPath.IndexOf(stem, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            part = p;
                            break;
                        }
                    }
                }
                if (part == null)
                {
                    sb.AppendLine("  产物未定位为 Part（type=" + committed.GetType().Name + "）——批处理显示态结论，Q2/Q3 判读暂停");
                    return;
                }
                try
                {
                    session.Parts.SetWork(part);
                }
                catch (Exception ex)
                {
                    sb.AppendLine("  SetWork 失败: " + Short(ex));
                }
                DumpPartInfo(sb, part);
                if (!importToWorkPart)
                {
                    TryAttachCamSetup(sb, part);
                }
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException != null ? " | inner: " + Short(ex.InnerException) : "";
                var stack = ex.StackTrace != null ? " | @" + TopFrame(ex) : "";
                sb.AppendLine("  " + factoryName + " 失败: " + Short(ex) + inner + stack);
            }
            finally
            {
                if (imp != null)
                {
                    try
                    {
                        imp.GetType().GetMethod("Destroy").Invoke(imp, null);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        /// <summary>Q2-路径A：翻译产物（新 part）上直接挂 CAMSetup——重建载体可行性。
        /// 注意：无 CAM 的零件读 .CAMSetup 属性即抛（非返 null），须吞掉再走 CreateCamSetup。</summary>
        private static void TryAttachCamSetup(StringBuilder sb, Part part)
        {
            try
            {
                if (part.CAMSetup != null)
                {
                    sb.AppendLine("  Q2: 产物已带 CAMSetup（STEP 原文件含制造数据？）");
                    return;
                }
            }
            catch (Exception)
            {
                // 无 CAMSetup 的零件：getter 抛 "Current part does not contain valid setup"——视为无，继续
            }
            try
            {
                SessionBootstrap.EnsureCamSession(Session.GetSession());
                var setup = part.CreateCamSetup(CamTemplate);
                sb.AppendLine("  Q2: 产物挂 CAMSetup(" + CamTemplate + "): " + (setup == null ? "null" : "ok"));
            }
            catch (Exception ex)
            {
                sb.AppendLine("  Q2: 产物挂 CAMSetup 失败: " + Short(ex));
            }
        }

        private static void DumpPartInfo(StringBuilder sb, Part part)
        {
            try
            {
                sb.AppendLine("  part: type=" + part.GetType().Name + " leaf=" + part.Leaf
                    + (part == Session.GetSession().Parts.Work ? " [Work]" : ""));
            }
            catch (Exception ex)
            {
                sb.AppendLine("  part 基本信息失败: " + Short(ex));
            }
            try
            {
                sb.AppendLine("  bodies=" + CountBodies(part));
            }
            catch (Exception ex)
            {
                sb.AppendLine("  bodies 统计失败: " + Short(ex));
            }
            try
            {
                sb.AppendLine("  CAMSetup: " + (part.CAMSetup == null ? "(null)" : "ok"));
            }
            catch (Exception)
            {
                sb.AppendLine("  CAMSetup: 无（getter 抛——STEP 件无制造数据，预期）");
            }
            try
            {
                var o = part.WCS.Origin;
                sb.AppendLine("  WCS 原点: [" + o.X.ToString("F6") + "," + o.Y.ToString("F6") + "," + o.Z.ToString("F6") + "]");
            }
            catch (Exception ex)
            {
                sb.AppendLine("  WCS 读取失败: " + Short(ex));
            }
        }

        private static string CountBodies(Part part)
        {
            return part.Bodies.ToArray().Length.ToString();
        }

        /// <summary>打开或定位（残留会话零件容错，同 JournalEntry.OpenOrFindSource 语义）。</summary>
        private static Part OpenOrFind(Session session, string partPath, StringBuilder sb)
        {
            try
            {
                var setup = SessionBootstrap.OpenPart(session, partPath);
                sb.AppendLine("对照件打开: " + (setup == null ? "(无 CAMSetup)" : "ok"));
                return session.Parts.Work;
            }
            catch (Exception ex)
            {
                sb.AppendLine("对照件直接打开失败: " + Short(ex));
            }
            try
            {
                foreach (Part p in session.Parts)
                {
                    if (string.Equals(p.FullPath, partPath, StringComparison.OrdinalIgnoreCase))
                    {
                        session.Parts.SetWork(p);
                        sb.AppendLine("对照件已开会话，定位设 Work: ok");
                        return p;
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("对照件枚举定位失败: " + Short(ex));
            }
            return null;
        }

        private static string Short(Exception ex)
        {
            var msg = ex.Message;
            return msg != null && msg.Length > 200 ? msg.Substring(0, 200) : msg;
        }

        private static string TopFrame(Exception ex)
        {
            var st = ex.StackTrace;
            if (string.IsNullOrEmpty(st))
            {
                return "?";
            }
            foreach (var line in st.Split('\n'))
            {
                var l = line.Trim();
                if (l.Length > 0 && l.IndexOf("NXOpen", StringComparison.Ordinal) >= 0)
                {
                    return l.Length > 160 ? l.Substring(0, 160) : l;
                }
            }
            return st.Split('\n')[0].Trim().Length > 160 ? st.Split('\n')[0].Trim().Substring(0, 160) : st.Split('\n')[0].Trim();
        }
    }
}
