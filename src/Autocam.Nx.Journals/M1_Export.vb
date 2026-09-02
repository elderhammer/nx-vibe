Option Strict Off
Imports System
Imports System.IO
Imports System.Reflection
Imports NXOpen

' M1 导出验证 journal：装载 Autocam.Nx.Adapter.dll（连同其全部依赖），
' 反射调用 JournalEntry.M1Export。逻辑全在 C# 侧，本文件只做装载。
' 运行：UGII_BATCH_MODE=1 "…\NXBIN\run_journal.exe" M1_Export.vb
Module M1ExportJournal
    Sub Main()
        Dim outDir As String = "C:\nx-vibe-journal-out"
        ' 公制模板（英制模板的数值口径是 inch，会污染 mm 口径）
        Dim partPath As String = outDir & "\parts\m1_template_metric.prt"
        If Not File.Exists(partPath) Then
            Dim tplDir As String = Session.GetSession().GetEnvironmentVariableValue("UGII_CAM_TEMPLATE_PART_METRIC_DIR")
            File.Copy(tplDir & "mill_planar.prt", partPath)
        End If
        Dim schemaPath As String = "C:\Users\21505\Code\nx-vibe\schema\autocam-plan.schema.json"
        Dim adapterBin As String = "C:\Users\21505\Code\nx-vibe\src\Autocam.Nx.Adapter\bin\Debug\net48"
        Try
            ' 依赖序装载（先依赖后消费方）
            LoadFrom(adapterBin, "Newtonsoft.Json.dll")
            LoadFrom(adapterBin, "NJsonSchema.dll")
            LoadFrom(adapterBin, "Autocam.Plan.Core.dll")
            LoadFrom(adapterBin, "Autocam.PlanExporter.Core.dll")
            LoadFrom(adapterBin, "Autocam.PlanExecutor.Core.dll")
            LoadFrom(adapterBin, "Autocam.PlanComparer.Core.dll")
            Dim adapter As Assembly = LoadFrom(adapterBin, "Autocam.Nx.Adapter.dll")

            ' NX 自带 AssemblyResolveHandler 有 bug（Type.GetType 带程序集名会崩）→ 从实例取类型
            Dim t As Type = adapter.GetType("Autocam.Nx.Adapter.Journals.JournalEntry")
            Dim m As MethodInfo = t.GetMethod("M1Export")
            Dim result As Object = m.Invoke(Nothing, New Object() {partPath, schemaPath, outDir})
            Console.WriteLine(result.ToString())
        Catch ex As Exception
            Dim err As String = "M1 FATAL: " & ex.ToString()
            Console.WriteLine(err)
            File.WriteAllText(outDir & "\m1_fatal.txt", err)
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
