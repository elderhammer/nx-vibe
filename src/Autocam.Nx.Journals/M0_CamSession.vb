Option Strict Off
Imports System
Imports System.IO
Imports System.Text
Imports System.Reflection
Imports NXOpen

' M0：CAMSession 全成员 + 模板注册表相关环境变量。输出 m0_camsession.txt。
Module M0CamSession
    Private sb As StringBuilder = New StringBuilder()
    Private outPath As String = "C:\nx-vibe-journal-out\m0_camsession.txt"

    Sub Main()
        Try
            Dim s As Session = Session.GetSession()
            Log("=== M0 CAMSession 探针 ===")
            Dim part1 As Part = s.Parts.NewDisplay("m0_cs", Part.Units.Millimeters)
            s.Parts.SetWork(part1)
            If Not s.IsCamSessionInitialized Then s.CreateCamSession()
            Dim cs As Object = s.CAMSession
            If cs Is Nothing Then
                Log("CAMSession: (null)")
            Else
                Log("CAMSession 类型: " & cs.GetType().FullName)
                Log("--- CAMSession 方法 ---")
                For Each mi As MethodInfo In cs.GetType().GetMethods(BindingFlags.Public Or BindingFlags.Instance Or BindingFlags.Static Or BindingFlags.DeclaredOnly)
                    Dim ps As String = ""
                    For Each p As ParameterInfo In mi.GetParameters()
                        ps += p.ParameterType.Name & " " & p.Name & ", "
                    Next
                    Log("  M " & mi.ReturnType.Name & " " & mi.Name & "(" & ps.TrimEnd(", ".ToCharArray()) & ")")
                Next
            End If

            Log("--- 相关环境变量 ---")
            For Each v As String In New String() {"UGII_CAM_TEMPLATE_SET_DIR", "UGII_CAM_RESOURCE_DIR", "UGII_CAM_TEMPLATE_PART_METRIC_DIR", "UGII_CAM_CONFIG_DIR"}
                Log("  " & v & " = " & s.GetEnvironmentVariableValue(v))
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
