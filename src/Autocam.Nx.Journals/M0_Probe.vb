Option Strict Off
Imports System
Imports System.IO
Imports NXOpen

' M0 探针：验证 run_journal 批处理模式可用 + 会话基本信息。
' 结果写 C:\nx-vibe-journal-out\m0_probe.txt（批处理无控制台，不依赖 stdout 捕获）。
Module M0Probe
    Sub Main()
        Dim outPath As String = "C:\nx-vibe-journal-out\m0_probe.txt"
        Try
            Dim s As Session = Session.GetSession()
            Dim lines As String = ""
            lines += "session ok" & vbCrLf
            lines += "base_version: " & s.GetEnvironmentVariableValue("UGII_BASE_VERSION") & vbCrLf
            Dim work As Part = s.Parts.Work
            lines += "work_part: " & If(work Is Nothing, "(none)", work.FullPath) & vbCrLf
            lines += "parts_iterator: " & WorkPartCount(s).ToString() & vbCrLf
            File.WriteAllText(outPath, lines)
        Catch ex As Exception
            File.WriteAllText(outPath, "PROBE FAILED: " & ex.ToString())
        End Try
    End Sub

    Private Function WorkPartCount(ByVal s As Session) As Integer
        Dim n As Integer = 0
        For Each p As Part In s.Parts
            n += 1
        Next
        Return n
    End Function
End Module
