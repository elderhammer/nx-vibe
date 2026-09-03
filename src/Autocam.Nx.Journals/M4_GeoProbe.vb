Option Strict Off
Imports System
Imports System.IO
Imports System.Reflection
Imports NXOpen

' M4 子步0 几何探针 v4 journal（GUI 会话执行）：工序↔关联几何读取链路实证。
' ⚠ v3 批处理实证：几何列表（CAM.Geometry set 内容/边界成员）在批处理加载态全为空容器
' ——几何数据会话耦合（与 M0 模板注册表同源的三态差异），须 GUI 显示态才物化。因此本
' journal 编译为 exe 供 File → Execute → NX Open 在真 GUI 会话跑（同 M2/M3 运行方式）。
' 四问：P1 几何角色目录 / P2 载荷形态 / P3 面属性 API / P4 确定性（核对清单 M4 节）。
' 编译：vbc /target:exe /out:C:\nx-vibe-journal-out\M4_GeoProbe.exe /r:System.dll /r:System.Core.dll
'       /r:"…\NXBIN\managed\NXOpen.dll" /r:"…\NXBIN\managed\NXOpen.Utilities.dll" M4_GeoProbe.vb
' 输出：C:\nx-vibe-journal-out\m4_geoprobe.txt
Module M4GeoProbeJournal
    Sub Main()
        Dim outDir As String = "C:\nx-vibe-journal-out"
        Dim partA As String = outDir & "\parts\m4_ground_truth.prt"
        Dim partB As String = ""
        Dim adapterBin As String = "C:\Users\21505\Code\nx-vibe\src\Autocam.Nx.Adapter\bin\Debug\net48"
        Try
            LoadFrom(adapterBin, "Newtonsoft.Json.dll")
            LoadFrom(adapterBin, "NJsonSchema.dll")
            LoadFrom(adapterBin, "Autocam.Plan.Core.dll")
            LoadFrom(adapterBin, "Autocam.PlanExporter.Core.dll")
            Dim adapter As Assembly = LoadFrom(adapterBin, "Autocam.Nx.Adapter.dll")
            Dim t As Type = adapter.GetType("Autocam.Nx.Adapter.Journals.JournalEntry")
            Dim m As MethodInfo = t.GetMethod("M4GeoProbe")
            Dim result As Object = m.Invoke(Nothing, New Object() {partA, partB, outDir})
            Console.WriteLine(result.ToString())
        Catch ex As Exception
            Dim err As String = "M4 FATAL: " & ex.ToString()
            Console.WriteLine(err)
            File.WriteAllText(outDir & "\m4_fatal.txt", err)
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
