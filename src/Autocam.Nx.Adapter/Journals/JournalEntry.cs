using System;
using System.IO;
using System.Text;
using Autocam.Nx.Adapter.Export;
using Autocam.Plan.Core.Diagnostics;
using Autocam.Plan.Core.Dto;
using Autocam.Plan.Core.Serialization;
using Autocam.PlanComparer.Core.Compare;
using Autocam.PlanComparer.Core.Report;
using Autocam.PlanExporter.Core.Export;
using NXOpen;

namespace Autocam.Nx.Adapter.Journals
{
    /// <summary>
    /// Journal 入口（VB journal 经 Assembly.LoadFrom + 反射调用本类静态方法——
    /// 全部逻辑留在 C# 侧，编译期类型安全）。各里程碑核对点见 nx-journal-manual-verification.md。
    /// </summary>
    public static class JournalEntry
    {
        /// <summary>
        /// M1：导出验证。打开零件 → 快照 → 导出 → schema 校验 → 再导一次（确定性字节对比）。
        /// 输出 outDir/plan.json + outDir/m1_export.txt。
        /// </summary>
        public static string M1Export(string partPath, string schemaPath, string outDir)
        {
            var sb = new StringBuilder();
            var session = Session.GetSession();
            var camSetup = SessionBootstrap.OpenPart(session, partPath);
            sb.AppendLine("M1 导出验证: " + partPath);
            sb.AppendLine("CAMSetup: " + (camSetup == null ? "(null)" : "ok"));

            var diag = new DiagnosticsCollector();
            var snapshot = NxSnapshotReader.Read(camSetup, Path.GetFileNameWithoutExtension(partPath), partPath, diag);
            var plan1 = PlanExportPipeline.Export(snapshot, new CapabilityProfile());
            var json1 = PlanSerializer.Serialize(plan1);

            var validator = PlanSchemaValidator.LoadAsync(schemaPath).GetAwaiter().GetResult();
            var errors = validator.Validate(json1);
            sb.AppendLine("schema 校验错误数: " + errors.Count);

            // 确定性：重读 + 重导（只读纪律保证会话状态不变）
            var diag2 = new DiagnosticsCollector();
            var snapshot2 = NxSnapshotReader.Read(camSetup, Path.GetFileNameWithoutExtension(partPath), partPath, diag2);
            var plan2 = PlanExportPipeline.Export(snapshot2, new CapabilityProfile());
            var json2 = PlanSerializer.Serialize(plan2);
            sb.AppendLine("两次导出字节级一致: " + (json1 == json2));

            sb.AppendLine("operations: " + plan1.Operations.Count);
            sb.AppendLine("tools: " + plan1.Resources.Tools.Count);
            sb.AppendLine("setups: " + plan1.Setups.Count);
            sb.AppendLine("workplan 元素: " + plan1.Workplan.Elements.Count);
            foreach (var d in diag.Entries)
            {
                sb.AppendLine("  [适配层诊断] " + d.Level + " " + d.Code + " " + d.Detail);
            }
            foreach (var d in plan1.Diagnostics)
            {
                sb.AppendLine("  [plan 诊断] " + d.Level + " " + d.Code + " " + d.Detail);
            }

            File.WriteAllText(Path.Combine(outDir, "plan.json"), json1);
            var report = sb.ToString();
            File.WriteAllText(Path.Combine(outDir, "m1_export.txt"), report);
            return report;
        }

        /// <summary>
        /// M3 部分闭环（批处理下可跑的部分）：真实 NX 零件导出两次 → plan/plan″
        /// → PlanComparer 自对比 → 报告 schema 校验。零偏差期望 = 导出确定性的
        /// 比较器侧验证（nx-adapter.md §3 闭环同构性质的批处理预演）。
        /// 输出 outDir/m3_report.json + outDir/m3_partial.txt。
        /// </summary>
        public static string M3Partial(string partPath, string planSchemaPath, string reportSchemaPath, string outDir)
        {
            var sb = new StringBuilder();
            var session = Session.GetSession();
            var camSetup = SessionBootstrap.OpenPart(session, partPath);
            sb.AppendLine("M3 部分闭环: " + partPath);

            var diag = new DiagnosticsCollector();
            var plan = PlanExportPipeline.Export(
                NxSnapshotReader.Read(camSetup, Path.GetFileNameWithoutExtension(partPath), partPath, diag),
                new CapabilityProfile());
            var plan2 = PlanExportPipeline.Export(
                NxSnapshotReader.Read(camSetup, Path.GetFileNameWithoutExtension(partPath), partPath, new DiagnosticsCollector()),
                new CapabilityProfile());
            sb.AppendLine("两次导出字节级一致: " + (PlanSerializer.Serialize(plan) == PlanSerializer.Serialize(plan2)));

            var compare = PlanComparePipeline.Compare(plan, plan2, new CompareContext());
            var reportJson = ReportSerializer.Serialize(compare);
            sb.AppendLine("deviations: " + compare.Deviations.Count);
            sb.AppendLine("scores: " + compare.Scores.StructureConsistency + " / " + compare.Scores.ParamDeviationMean + " / " + compare.Scores.GeometryMatchRate);

            var reportValidator = PlanSchemaValidator.LoadAsync(reportSchemaPath).GetAwaiter().GetResult();
            sb.AppendLine("报告 schema 校验错误数: " + reportValidator.Validate(reportJson).Count);

            File.WriteAllText(Path.Combine(outDir, "m3_report.json"), reportJson);
            var report = sb.ToString();
            File.WriteAllText(Path.Combine(outDir, "m3_partial.txt"), report);
            return report;
        }

        /// <summary>
        /// M2 重建入口：plan.json → 命令序列 → NXOpen 执行（GUI 会话跑——批处理下对象模板
        /// 注册表不加载，Create 必失败；失败如实输出，不静默）。核对清单 M2 节。
        /// </summary>
        public static string M2Rebuild(string partPath, string planJsonPath, string planSchemaPath, string outDir)
        {
            var sb = new StringBuilder();
            var session = Session.GetSession();
            sb.AppendLine("M2 重建: " + partPath);

            var validator = PlanSchemaValidator.LoadAsync(planSchemaPath).GetAwaiter().GetResult();
            var plan = PlanDeserializer.Deserialize(File.ReadAllText(planJsonPath), validator);
            var build = Autocam.PlanExecutor.Core.Build.PlanExecutorPipeline.Build(plan, new CapabilityProfile());
            sb.AppendLine("命令数: " + build.Commands.Count);

            try
            {
                // 预热：先打开源 CAM 零件——run_journal 会话下打开制造业 part 会触发加工应用
                // 挂载组/工序模板注册表（M0_Templates 实证注册表仅 UI 网关加载）。
                // 真 GUI 会话中零件已在会话里（OpenDisplay 会抛「文件已存在」），跳过即可——
                // 该会话由用户手动进入加工环境，注册表已就绪。
                try
                {
                    var sourceSetup = SessionBootstrap.OpenPart(session, partPath);
                    sb.AppendLine("源零件打开（模板预热）: " + (sourceSetup == null ? "(无 CAMSetup)" : "ok"));
                }
                catch (Exception preheatEx)
                {
                    sb.AppendLine("源零件打开（模板预热）跳过: " + preheatEx.Message);
                }
                // D-适配-3：另存副本载体（几何同源、零 STEP 导入风险）
                var camSetup = SessionBootstrap.NewPartWithCamSetup(session, "rebuild_part");
                sb.AppendLine("新建重建 part: ok");
                sb.AppendLine("重建 part 根成员: " + DumpViewRoots(camSetup));
                var diag = new DiagnosticsCollector();
                Autocam.Nx.Adapter.Rebuild.NxCommandExecutor.Execute(
                    camSetup, build.Commands, diag, msg => sb.AppendLine("  " + msg));
                sb.AppendLine("命令执行: 全部完成（" + build.Commands.Count + " 条）");
                foreach (var d in diag.Entries)
                {
                    sb.AppendLine("  [诊断] " + d.Level + " " + d.Code + " " + d.Detail);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("命令执行失败: " + ex.Message);
            }

            var report = sb.ToString();
            File.WriteAllText(Path.Combine(outDir, "m2_rebuild.txt"), report);
            return report;
        }

        /// <summary>
        /// M3 完整闭环（GUI 会话）：导出 plan → 重建 prj′（另存副本）→ 再导出 plan″ →
        /// Compare(plan, plan″) → 报告 schema 校验 → 落盘。闭环验收 = deviations 全部可归因
        /// （nx-adapter.md §3 闭环同构性质在真实 NX 的执行）。
        /// 输出 outDir/m3_report.json + outDir/m3_loop.txt + outDir/plan.json（plan″）。
        /// </summary>
        public static string M3Loop(string partPath, string planSchemaPath, string reportSchemaPath, string outDir)
        {
            var sb = new StringBuilder();
            var session = Session.GetSession();
            sb.AppendLine("M3 完整闭环: " + partPath);

            var validator = PlanSchemaValidator.LoadAsync(planSchemaPath).GetAwaiter().GetResult();
            var reportValidator = PlanSchemaValidator.LoadAsync(reportSchemaPath).GetAwaiter().GetResult();

            // 预热：打开/定位源零件并设为 Work（残留会话零件不能当源——Work 须指源）
            var sourceCam = OpenOrFindSource(session, partPath, sb);
            if (sourceCam == null)
            {
                sb.AppendLine("FATAL: 未找到源零件 " + partPath + "（M3 闭环中止）");
                var fail = sb.ToString();
                File.WriteAllText(Path.Combine(outDir, "m3_loop.txt"), fail);
                return fail;
            }

            // ① 导出 ground truth plan
            var diag1 = new DiagnosticsCollector();
            var sourceCamSetup = sourceCam;
            var plan = PlanExportPipeline.Export(
                NxSnapshotReader.Read(sourceCamSetup, Path.GetFileNameWithoutExtension(partPath), partPath, diag1),
                new CapabilityProfile());
            var planJson = PlanSerializer.Serialize(plan);
            File.WriteAllText(Path.Combine(outDir, "plan.json"), planJson);
            sb.AppendLine("① 导出 plan: " + plan.Operations.Count + " 工序 / " + plan.Resources.Tools.Count
                + " 刀具 / " + plan.Setups.Count + " setup / workplan " + plan.Workplan.Elements.Count + " 元素");
            sb.AppendLine("plan schema 校验错误数: " + validator.Validate(planJson).Count);

            // ② 重建（M2 同路径：新 part + 命令执行）
            var build = Autocam.PlanExecutor.Core.Build.PlanExecutorPipeline.Build(plan, new CapabilityProfile());
            sb.AppendLine("② 命令数: " + build.Commands.Count);
            try
            {
                var camSetup = SessionBootstrap.NewPartWithCamSetup(session, "rebuild_part");
                var diag2 = new DiagnosticsCollector();
                Autocam.Nx.Adapter.Rebuild.NxCommandExecutor.Execute(
                    camSetup, build.Commands, diag2, msg => sb.AppendLine("  " + msg));
                sb.AppendLine("② 命令执行: 全部完成（" + build.Commands.Count + " 条）");
                foreach (var d in diag2.Entries)
                {
                    sb.AppendLine("  [执行诊断] " + d.Level + " " + d.Code + " " + d.Detail);
                }
                try
                {
                    var prjPath = Path.Combine(outDir, "parts", "rebuild_part.prt");
                    if (File.Exists(prjPath))
                    {
                        File.Delete(prjPath);   // 旧 prj′ 覆盖（重复跑 M3 时 SaveAs 撞名必失败）
                    }
                    session.Parts.Work.SaveAs(prjPath);
                    sb.AppendLine("② prj′ 已落盘: rebuild_part.prt");
                }
                catch (Exception saveEx)
                {
                    sb.AppendLine("② prj′ 落盘失败: " + saveEx.Message);
                }

                // ③ 再导出 plan″ → Compare
                var diag3 = new DiagnosticsCollector();
                var plan2 = PlanExportPipeline.Export(
                    NxSnapshotReader.Read(camSetup, "rebuild_part", Path.Combine(outDir, "parts", "rebuild_part.prt"), diag3),
                    new CapabilityProfile());
                var plan2Json = PlanSerializer.Serialize(plan2);
                File.WriteAllText(Path.Combine(outDir, "plan_rebuilt.json"), plan2Json);
                sb.AppendLine("③ 导出 plan″: " + plan2.Operations.Count + " 工序 / " + plan2.Resources.Tools.Count
                    + " 刀具 / " + plan2.Setups.Count + " setup / workplan " + plan2.Workplan.Elements.Count + " 元素");
                sb.AppendLine("plan″ schema 校验错误数: " + validator.Validate(plan2Json).Count);

                var compare = PlanComparePipeline.Compare(plan, plan2, BuildLoopCompareContext());
                var reportJson = ReportSerializer.Serialize(compare);
                sb.AppendLine("④ deviations: " + compare.Deviations.Count);
                sb.AppendLine("④ scores: structure=" + compare.Scores.StructureConsistency
                    + " param=" + compare.Scores.ParamDeviationMean
                    + " geometry=" + compare.Scores.GeometryMatchRate);
                foreach (var d in compare.Diagnostics)
                {
                    sb.AppendLine("  [比较诊断] " + d.Level + " " + d.Code + " " + d.Detail);
                }
                sb.AppendLine("报告 schema 校验错误数: " + reportValidator.Validate(reportJson).Count);
                File.WriteAllText(Path.Combine(outDir, "m3_report.json"), reportJson);
            }
            catch (Exception ex)
            {
                sb.AppendLine("② 重建失败: " + ex.Message);
            }

            var report = sb.ToString();
            File.WriteAllText(Path.Combine(outDir, "m3_loop.txt"), report);
            return report;
        }

        /// <summary>
        /// 闭环比较上下文：注入 NX 写保护表（NxWriteProtection，M3_Probe E 段实测）——
        /// 比较侧按 (工序类型, 字段) 结构化豁免，与执行侧跳过写入同源同表（绝不静默）。
        /// </summary>
        private static CompareContext BuildLoopCompareContext()
        {
            var profile = new CapabilityProfile();
            foreach (var pair in Autocam.Nx.Adapter.Policies.NxWriteProtection.FieldsByPlanType)
            {
                profile.UnwritableByPlanType[pair.Key] =
                    new System.Collections.Generic.HashSet<string>(pair.Value);
            }
            return new CompareContext { RightCapability = profile };
        }

        /// <summary>
        /// 打开或定位源零件并设为 Work：未开 → OpenPart；已开会话 → FullPath 匹配查找 + SetWork
        /// （残留会话零件可能占着 Work，不能默认）。
        /// </summary>
        private static NXOpen.CAM.CAMSetup OpenOrFindSource(Session session, string partPath, System.Text.StringBuilder sb)
        {
            try
            {
                var setup = SessionBootstrap.OpenPart(session, partPath);
                sb.AppendLine("源零件打开: " + (setup == null ? "(无 CAMSetup)" : "ok"));
                return setup;
            }
            catch (Exception ex)
            {
                sb.AppendLine("源零件已开会话: " + ex.Message);
            }
            try
            {
                foreach (NXOpen.Part p in session.Parts)
                {
                    if (string.Equals(p.FullPath, partPath, StringComparison.OrdinalIgnoreCase))
                    {
                        session.Parts.SetWork(p);
                        sb.AppendLine("源零件定位并设 Work: ok");
                        return p.CAMSetup;
                    }
                }
            }
            catch (Exception enumEx)
            {
                sb.AppendLine("枚举已开零件失败: " + enumEx.Message);
            }
            return null;
        }

        /// <summary>调试辅助：dump 新 part 四视图根的直接子组名（判别名冲突来源）。</summary>
        private static string DumpViewRoots(NXOpen.CAM.CAMSetup camSetup)
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (NXOpen.CAM.CAMSetup.View view in System.Enum.GetValues(typeof(NXOpen.CAM.CAMSetup.View)))
            {
                var root = camSetup.GetRoot(view);
                var children = new System.Collections.Generic.List<string>();
                if (root != null)
                {
                    foreach (NXOpen.CAM.CAMObject member in root.GetMembers())
                    {
                        var g = member as NXOpen.CAM.NCGroup;
                        children.Add(g != null ? g.Name : "(" + member.GetType().Name + ")");
                    }
                }
                names.Add(view + "=[" + string.Join(",", children) + "]");
            }
            return string.Join(" | ", names);
        }
    }
}
