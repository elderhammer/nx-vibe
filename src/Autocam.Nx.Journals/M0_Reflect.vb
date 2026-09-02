Option Strict Off
Imports System
Imports System.IO
Imports System.Text
Imports System.Reflection
Imports NXOpen

' M0 反射探测 v2：批处理下 CreateCamSetup 返回 null → 改走「打开 CAM 模板零件」。
' 目标：拿到 CAMSetup 真实 API 面（视图/组集合/Builder 工厂/切参/枚举/许可）。
' 输出 m0_reflect.txt。运行：UGII_BATCH_MODE=1 "…\NXBIN\run_journal.exe" M0_Reflect.vb
Module M0Reflect
    Private sb As StringBuilder = New StringBuilder()
    Private outPath As String = "C:\nx-vibe-journal-out\m0_reflect.txt"

    Sub Main()
        Try
            Dim s As Session = Session.GetSession()
            Log("=== M0 反射探测 v2 ===")

            Log("--- Session 许可相关成员 ---")
            DumpMembers(GetType(Session), "License")
            DumpMembers(GetType(Session), "Cam")

            ' 模板零件：复制 + 打开（自带 CAMSetup，绕开批处理下 CreateCamSetup 返 null）
            Dim tplDir As String = s.GetEnvironmentVariableValue("UGII_CAM_TEMPLATE_PART_ENGLISH_DIR")
            Log("模板目录: " & tplDir)
            Dim src As String = tplDir & "mill_planar.prt"
            If Not File.Exists(src) Then src = tplDir & "\mill_planar.prt"
            Log("模板文件存在: " & File.Exists(src))
            Dim workDir As String = "C:\nx-vibe-journal-out\parts"
            Directory.CreateDirectory(workDir)
            Dim dst As String = workDir & "\m0_template.prt"
            If File.Exists(dst) Then File.Delete(dst)
            File.Copy(src, dst)

            Dim loadStatus As PartLoadStatus = Nothing
            Dim part1 As Part = s.Parts.Open(dst, loadStatus)
            Log("打开模板零件: " & part1.Name)

            Dim camSetup As NXOpen.CAM.CAMSetup = part1.CAMSetup
            If camSetup Is Nothing Then
                Log("FATAL: 模板零件无 CAMSetup")
                File.WriteAllText(outPath, sb.ToString())
                Return
            End If
            Log("CAMSetup 就绪: " & camSetup.GetType().FullName)

            Log("--- CAMSetup 成员（View/Group/Builder/Collection/Root）---")
            DumpMembers(camSetup.GetType(), "View")
            DumpMembers(camSetup.GetType(), "Group")
            DumpMembers(camSetup.GetType(), "Builder")
            DumpMembers(camSetup.GetType(), "Collection")
            DumpMembers(camSetup.GetType(), "Root")

            Log("--- CAMSetup+View 枚举值 ---")
            DumpEnumValues(GetType(NXOpen.CAM.CAMSetup).GetNestedType("View"))

            Log("--- NCGroupCollection 方法（Create/FindObject）---")
            DumpMethods(GetType(NXOpen.CAM.NCGroupCollection), "Create")
            DumpMethods(GetType(NXOpen.CAM.NCGroupCollection), "FindObject")

            Log("--- CutParameters 成员 ---")
            DumpMembers(GetType(NXOpen.CAM.CutParameters), "")
            Log("--- MillCutParameters 成员 ---")
            DumpMembers(GetType(NXOpen.CAM.MillCutParameters), "")
            Log("--- HoleMachiningCutParameters 成员 ---")
            DumpMembers(GetType(NXOpen.CAM.HoleMachiningCutParameters), "")

            Log("--- Operation 成员（Parent/Tool/Geometry）---")
            DumpMembers(GetType(NXOpen.CAM.Operation), "Parent")
            DumpMembers(GetType(NXOpen.CAM.Operation), "Tool")
            DumpMembers(GetType(NXOpen.CAM.Operation), "Geometry")

            Log("--- CAMSetup 含 Create 的方法（Builder 工厂全景）---")
            DumpMethods(camSetup.GetType(), "Create")

            Log("=== 反射探测结束 ===")
        Catch ex As Exception
            Log("FATAL: " & ex.ToString())
        End Try
        File.WriteAllText(outPath, sb.ToString())
    End Sub

    Private Sub DumpMethods(ByVal t As Type, ByVal filter As String)
        For Each m As MethodInfo In t.GetMethods(BindingFlags.Public Or BindingFlags.Instance Or BindingFlags.Static Or BindingFlags.DeclaredOnly)
            If filter.Length = 0 OrElse m.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 Then
                Dim ps As String = ""
                For Each p As ParameterInfo In m.GetParameters()
                    ps += p.ParameterType.Name & " " & p.Name & ", "
                Next
                Log("  M " & m.ReturnType.Name & " " & m.Name & "(" & ps.TrimEnd(", ".ToCharArray()) & ")")
            End If
        Next
    End Sub

    Private Sub DumpMembers(ByVal t As Type, ByVal filter As String)
        If t Is Nothing Then
            Log("  (类型不存在)")
            Return
        End If
        For Each m As MemberInfo In t.GetMembers(BindingFlags.Public Or BindingFlags.Instance Or BindingFlags.Static Or BindingFlags.DeclaredOnly)
            If filter.Length = 0 OrElse m.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 Then
                Log("  " & m.MemberType.ToString() & " " & m.Name)
            End If
        Next
    End Sub

    Private Sub DumpEnumValues(ByVal t As Type)
        If t Is Nothing Then
            Log("  (枚举类型不存在)")
            Return
        End If
        For Each name As String In System.Enum.GetNames(t)
            Log("  " & name)
        Next
    End Sub

    Private Sub Log(ByVal msg As String)
        sb.AppendLine(msg)
        Console.WriteLine(msg)
    End Sub
End Module
