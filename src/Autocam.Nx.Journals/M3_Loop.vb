Option Strict Off
Imports System
Imports System.IO
Imports System.Reflection

' M3 完整闭环 journal 装载器（GUI 会话跑，编译为 exe 供 File → Execute → NX Open）：
' 导出 plan → 重建 prj′ → 再导出 plan″ → Compare → 报告。逻辑全在 C# 侧 JournalEntry.M3Loop。
' 编译：vbc /target:exe /out:C:\nx-vibe-journal-out\M3_Loop.exe /r:System.dll /r:System.Core.dll
'       /r:"…\NXBIN\managed\NXOpen.dll" /r:"…\NXBIN\managed\NXOpen.Utilities.dll" M3_Loop.vb
Module M3LoopJournal
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
            Dim m As MethodInfo = t.GetMethod("M3Loop")
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
