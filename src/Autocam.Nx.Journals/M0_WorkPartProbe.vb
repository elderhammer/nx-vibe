Option Strict Off
Imports System
Imports System.IO
Imports System.Text
Imports NXOpen

' M0 补充探针：验证「work part + CAM 会话初始化」是否是 CreateCamSetup 返 null 的原因。
' 输出 m0_workpart.txt。
Module M0WorkPartProbe
    Private sb As StringBuilder = New StringBuilder()
    Private outPath As String = "C:\nx-vibe-journal-out\m0_workpart.txt"

    Sub Main()
        Try
            Dim s As Session = Session.GetSession()
            Log("=== M0 补充探针 ===")

            Dim part1 As Part = s.Parts.NewDisplay("m0_wp", Part.Units.Millimeters)
            Log("NewDisplay: " & part1.Name)
            Log("WorkPart 初始: " & If(s.Parts.Work Is part1, "是本零件", If(s.Parts.Work Is Nothing, "(null)", s.Parts.Work.Name)))
            s.Parts.SetWork(part1)
            Log("SetWork 后 WorkPart: " & If(s.Parts.Work Is part1, "是本零件 ✓", "否"))
            Log("IsCamSessionInitialized: " & s.IsCamSessionInitialized.ToString())
            If Not s.IsCamSessionInitialized Then
                s.CreateCamSession()
                Log("CreateCamSession 调用后: " & s.IsCamSessionInitialized.ToString())
            End If

            For Each tpl As String In New String() {"mill_planar", "hole_making"}
                Try
                    Dim cs As NXOpen.CAM.CAMSetup = part1.CreateCamSetup(tpl)
                    Log("CreateCamSetup(" & tpl & "): " & If(cs Is Nothing, "null", "非null"))
                Catch ex As Exception
                    Log("CreateCamSetup(" & tpl & ") 异常: " & ex.Message)
                End Try
            Next
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
