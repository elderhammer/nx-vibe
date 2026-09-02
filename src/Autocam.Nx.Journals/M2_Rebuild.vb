Option Strict Off
Imports System
Imports System.IO
Imports System.Reflection
Imports NXOpen

' M2 重建 journal：plan.json → 命令序列 → NXOpen 执行。
' ⚠ 批处理下对象模板注册表不加载（M0 实测），Create 会失败——失败如实输出；
' 完整执行验证在交互式 NX GUI 会话跑本 journal（核对清单 M2 节）。
' 运行：UGII_BATCH_MODE=1 "…\NXBIN\run_journal.exe" M2_Rebuild.vb
Module M2RebuildJournal
    Sub Main()
        Dim outDir As String = "C:\nx-vibe-journal-out"
        Dim partPath As String = outDir & "\parts\m1_template_metric.prt"
        Dim planJson As String = outDir & "\plan.json"
        Dim planSchema As String = "C:\Users\21505\Code\nx-vibe\schema\autocam-plan.schema.json"
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
            Dim m As MethodInfo = t.GetMethod("M2Rebuild")
            Dim result As Object = m.Invoke(Nothing, New Object() {partPath, planJson, planSchema, outDir})
            Console.WriteLine(result.ToString())
        Catch ex As Exception
            Dim err As String = "M2 FATAL: " & ex.ToString()
            Console.WriteLine(err)
            File.WriteAllText(outDir & "\m2_fatal.txt", err)
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
