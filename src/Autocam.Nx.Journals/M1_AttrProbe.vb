Option Strict Off
Imports System
Imports System.IO
Imports System.Text
Imports NXOpen
Imports NXOpen.CAM

' M1 前置探针：操作类型名读取方式 + 模板自带工序的 Builder 读取可行性。
' 输出 m1_attrprobe.txt。
Module M1AttrProbe
    Private sb As StringBuilder = New StringBuilder()
    Private outPath As String = "C:\nx-vibe-journal-out\m1_attrprobe.txt"

    Sub Main()
        Try
            Dim s As Session = Session.GetSession()
            Log("=== M1 前置探针 ===")
            Dim dst As String = "C:\nx-vibe-journal-out\parts\m1_template.prt"
            Dim tplDir As String = s.GetEnvironmentVariableValue("UGII_CAM_TEMPLATE_PART_ENGLISH_DIR")
            If Not File.Exists(dst) Then File.Copy(tplDir & "mill_planar.prt", dst)
            Dim loadStatus As PartLoadStatus = Nothing
            Dim part1 As Part = s.Parts.OpenDisplay(dst, loadStatus)
            s.Parts.SetWork(part1)
            If Not s.IsCamSessionInitialized Then s.CreateCamSession()
            Dim camSetup As CAMSetup = part1.CAMSetup

            ' 取第一个工序做解剖
            Dim first As NXOpen.CAM.Operation = Nothing
            For Each o As NXOpen.CAM.Operation In camSetup.CAMOperationCollection
                first = o
                Exit For
            Next
            If first Is Nothing Then
                Log("FATAL: 无工序")
            Else
                Log("第一个工序: " & first.Name)
                Try
                    Log("GetNameOfType: " & first.GetNameOfType())
                Catch ex As Exception
                    Log("GetNameOfType ✗ " & ex.Message)
                End Try
                Log("JournalIdentifier: " & first.JournalIdentifier)
                ' string attributes
                Try
                    Dim titles() As NXOpen.NXObject.AttributeInformation = first.GetAttributeTitlesByType(NXObject.AttributeType.String)
                    Log("String 属性数: " & titles.Length.ToString())
                    For Each t As NXOpen.NXObject.AttributeInformation In titles
                        Try
                            Log("  [" & t.Title & "] = " & first.GetStringAttribute(t.Title))
                        Catch
                            Log("  [" & t.Title & "] (读失败)")
                        End Try
                    Next
                Catch ex As Exception
                    Log("GetAttributeTitlesByType ✗ " & ex.Message)
                End Try
            End If

            ' 对模板自带工序取 CavityMillingBuilder 读参数（批处理可行性验证）
            Try
                Dim cav As NXOpen.CAM.Operation = Nothing
                For Each o As NXOpen.CAM.Operation In camSetup.CAMOperationCollection
                    If o.Name.IndexOf("CAVITY", StringComparison.OrdinalIgnoreCase) >= 0 Then
                        cav = o
                        Exit For
                    End If
                Next
                If cav Is Nothing Then cav = first
                Dim cb As CavityMillingBuilder = camSetup.CAMOperationCollection.CreateCavityMillingBuilder(cav)
                Dim dp As InheritableDoubleBuilder = cb.DepthPerCut
                Log("CavityMillingBuilder(" & cav.Name & ") DepthPerCut: Value=" & dp.Value.ToString() & " Inherited=" & dp.InheritanceStatus.ToString())
                Dim fs As InheritableDoubleBuilder = cb.CutParameters.FloorStock
                Log("  FloorStock: Value=" & fs.Value.ToString() & " Inherited=" & fs.InheritanceStatus.ToString())
                Dim ws As InheritableDoubleBuilder = cb.CutParameters.WallStock
                Log("  WallStock: Value=" & ws.Value.ToString() & " Inherited=" & ws.InheritanceStatus.ToString())
                cb.Destroy()
            Catch ex As Exception
                Log("CavityMillingBuilder 读取 ✗ " & ex.ToString())
            End Try

            Log("=== 结束 ===")
        Catch ex As Exception
            Log("FATAL: " & ex.ToString())
        End Try
        File.WriteAllText(outPath, sb.ToString())
    End Sub

    Private Sub Log(ByVal msg As String)
        sb.AppendLine(msg)
        Console.WriteLine(msg)
    End Sub
End Module
