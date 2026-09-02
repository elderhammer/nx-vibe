Option Strict Off
Imports System
Imports System.IO
Imports System.Reflection
Imports NXOpen

' M3 部分闭环 journal（批处理可跑）：真实零件导出 ×2 → 比较器自对比 → 报告 schema 校验。
' 运行：UGII_BATCH_MODE=1 "…\NXBIN\run_journal.exe" M3_Partial.vb
Module M3PartialJournal
    Sub Main()
        Dim outDir As String = "C:\nx-vibe-journal-out"
        Dim partPath As String = outDir & "\parts\m1_template_metric.prt"
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
            Dim m As MethodInfo = t.GetMethod("M3Partial")
            Dim result As Object = m.Invoke(Nothing, New Object() {partPath, planSchema, reportSchema, outDir})
            Console.WriteLine(result.ToString())
        Catch ex As Exception
            Dim err As String = "M3 FATAL: " & ex.ToString()
            Console.WriteLine(err)
            File.WriteAllText(outDir & "\m3_fatal.txt", err)
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
