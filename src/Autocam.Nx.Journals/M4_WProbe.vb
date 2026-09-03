Option Strict Off
Imports System
Imports System.IO
Imports System.Reflection
Imports NXOpen

' M4 几何写探针 v2 journal（GUI 会话执行，零 UI 操作）：自建含显式面几何的 ground truth。
' 新零件 + CAMSetup → 程序化方块体 → 建 3 个显式挂面 FACE_MILL_ZIGZAG 工序
' （CutArea/PartGeometry 角色矩阵）→ 回读 → SaveAs m4_gt_face.prt → 导出验证 features anchor。
' 编译：vbc /target:exe /out:C:\nx-vibe-journal-out\M4_WProbe.exe /r:System.dll /r:System.Core.dll
'       /r:"…\NXBIN\managed\NXOpen.dll" /r:"…\NXBIN\managed\NXOpen.Utilities.dll" M4_WProbe.vb
' 运行：NX GUI 加工环境 → File → Execute → NX Open → M4_WProbe.exe
' 输出：C:\nx-vibe-journal-out\m4_wprobe.txt + parts\m4_gt_face.prt + parts\m4_gt_plan.json
Module M4WProbeJournal
    Sub Main()
        Dim outDir As String = "C:\nx-vibe-journal-out"
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
            Dim m As MethodInfo = t.GetMethod("M4GeoWriteProbe")
            Dim result As Object = m.Invoke(Nothing, New Object() {planSchema, outDir})
            Console.WriteLine(result.ToString())
        Catch ex As Exception
            Dim err As String = "M4W FATAL: " & ex.ToString()
            Console.WriteLine(err)
            File.WriteAllText(outDir & "\m4_wprobe_fatal.txt", err)
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
