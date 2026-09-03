Option Strict Off
Imports System
Imports System.IO
Imports System.Reflection

' M4c 主闭环 journal 装载器（GUI 会话跑——op 模板注册表仅 GUI 加载，同 M3_Loop）：
' 源件现导 plan → STEP 直开载体（S0 定案）重建 → 再导出 plan″ → Compare → 报告。
' 逻辑全在 C# 侧 JournalEntry.M4cLoop。载体 = parts\m4_gt_face.stp（S0 自动导出 fixture，AP214）。
' 编译：vbc /target:exe /out:C:\nx-vibe-journal-out\M4c_Loop.exe /r:System.dll /r:System.Core.dll
'       /r:"…\NXBIN\managed\NXOpen.dll" /r:"…\NXBIN\managed\NXOpen.Utilities.dll" M4c_Loop.vb
Module M4cLoopJournal
    Sub Main()
        Dim outDir As String = "C:\nx-vibe-journal-out"
        Dim sourcePath As String = outDir & "\parts\m1_template_metric.prt"
        Dim stepPath As String = outDir & "\parts\m4_gt_face.stp"
        Dim planSchema As String = "C:\Users\21505\Code\nx-vibe\schema\autocam-plan.schema.json"
        Dim reportSchema As String = "C:\Users\21505\Code\nx-vibe\schema\autocam-compare-report.schema.json"
        Dim adapterBin As String = "C:\Users\21505\Code\nx-vibe\src\Autocam.Nx.Adapter\bin\Debug\net48"
        Try
            LoadFrom(adapterBin, "Newtonsoft.Json.dll")
            LoadFrom(adapterBin, "NJsonSchema.dll")
            LoadFrom(adapterBin, "Autocam.Plan.Core.dll")
            LoadFrom(adapterBin, "Autocam.PlanExporter.Core.dll")
            LoadFrom(adapterBin, "Autocam.PlanExecutor.Core.dll")
            LoadFrom(adapterBin, "Autocam.PlanComparer.Core.dll")
            Dim adapter As Assembly = LoadFrom(adapterBin, "Autocam.Nx.Adapter.dll")
            Dim t As Type = adapter.GetType("Autocam.Nx.Adapter.Journals.JournalEntry")
            Dim m As MethodInfo = t.GetMethod("M4cLoop")
            Dim result As Object = m.Invoke(Nothing, New Object() {sourcePath, stepPath, planSchema, reportSchema, outDir})
            Console.WriteLine(result.ToString())
        Catch ex As Exception
            Dim err As String = "M4c FATAL: " & ex.ToString()
            Console.WriteLine(err)
            File.WriteAllText(outDir & "\m4c_loop_fatal.txt", err)
        End Try
    End Sub

    Private Function LoadFrom(ByVal dir As String, ByVal name As String) As Assembly
        Dim path As String = dir & "\" & name
        If File.Exists(path) Then
            Return Assembly.LoadFrom(path)
        End If
        Return Nothing
    End Function
End Module
