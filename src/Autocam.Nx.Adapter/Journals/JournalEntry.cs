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
                // D-适配-3：另存副本载体（几何同源、零 STEP 导入风险）
                var camSetup = SessionBootstrap.NewPartWithCamSetup(session, "rebuild_part");
                var diag = new DiagnosticsCollector();
                Autocam.Nx.Adapter.Rebuild.NxCommandExecutor.Execute(camSetup, build.Commands, diag);
                sb.AppendLine("命令执行: 全部完成（" + build.Commands.Count + " 条）");
                foreach (var d in diag.Entries)
                {
                    sb.AppendLine("  [诊断] " + d.Level + " " + d.Code + " " + d.Detail);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("命令执行失败（批处理下对象模板注册表不加载为已知限制；GUI 会话应可跑通）: " + ex.Message);
            }

            var report = sb.ToString();
            File.WriteAllText(Path.Combine(outDir, "m2_rebuild.txt"), report);
            return report;
        }
    }
}
