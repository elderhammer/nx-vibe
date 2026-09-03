Option Strict Off
Imports System
Imports System.IO
Imports System.Reflection
Imports NXOpen

' M4c S0 STEP 打开探针 journal：Q1-Q4 运行时实证（静态反射面先证于 m4c_reflect1-4.ps1，
' 结论链：Session.DexManager → CreateStep203/214/242Importer → InputFile/ImportTo → Commit）。
' 运行模式两态（Q4 判读对照）：
'   批处理: UGII_BATCH_MODE=1 "C:\Program Files\Siemens\NX2406\NXBIN\run_journal.exe" M4c_StepProbe.vb
'   GUI:    vbc 编译 exe → NX GUI → File → Execute → NX Open 选 M4c_StepProbe.exe（同 M2/M3/M4 方式）
' 编译：vbc /target:exe /out:C:\nx-vibe-journal-out\M4c_StepProbe.exe /r:System.dll /r:System.Core.dll
'       /r:"…\NXBIN\managed\NXOpen.dll" /r:"…\NXBIN\managed\NXOpen.Utilities.dll" M4c_StepProbe.vb
' 输出：C:\nx-vibe-journal-out\m4c_stepopen.txt
' S1：STEP fixture = parts\m4_gt_face.stp——缺失时探针自动从对照件导出（CreateStepCreator/Ap214；
' 实测 NX 输出文件名 = 零件名 m4_gt_face.stp，OutputFile 仅定目录）；自动导出失败才需 GUI 手动兜底。
Module M4cStepProbeJournal
    Sub Main()
        Dim outDir As String = "C:\nx-vibe-journal-out"
        Dim controlPath As String = outDir & "\parts\m4_gt_face.prt"
        Dim stepPath As String = outDir & "\parts\m4_gt_face.stp"
        Dim adapterBin As String = "C:\Users\21505\Code\nx-vibe\src\Autocam.Nx.Adapter\bin\Debug\net48"
        Try
            LoadFrom(adapterBin, "Newtonsoft.Json.dll")
            LoadFrom(adapterBin, "NJsonSchema.dll")
            LoadFrom(adapterBin, "Autocam.Plan.Core.dll")
            LoadFrom(adapterBin, "Autocam.PlanExporter.Core.dll")
            Dim adapter As Assembly = LoadFrom(adapterBin, "Autocam.Nx.Adapter.dll")
            Dim t As Type = adapter.GetType("Autocam.Nx.Adapter.Journals.JournalEntry")
            Dim m As MethodInfo = t.GetMethod("M4cStepProbe")
            Dim result As Object = m.Invoke(Nothing, New Object() {controlPath, stepPath, outDir})
            Console.WriteLine(result.ToString())
        Catch ex As Exception
            Dim err As String = "M4c FATAL: " & ex.ToString()
            Console.WriteLine(err)
            File.WriteAllText(outDir & "\m4c_fatal.txt", err)
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
