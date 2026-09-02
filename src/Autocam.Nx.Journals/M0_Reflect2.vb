Option Strict Off
Imports System
Imports System.IO
Imports System.Text
Imports System.Reflection
Imports NXOpen

' M0 反射探测第二轮：CAMOperationCollection（工序创建）/ CavityMillingBuilder（DepthPerCut）
' / MillOrientGeomBuilder（MCS）/ MillMethodBuilder / MillingToolBuilder / NCGroup /
' InheritableDoubleBuilder（未知点 1 继承态）/ HoleDrillingBuilder（cycle/depth）。
' 输出 m0_reflect2.txt。运行：UGII_BATCH_MODE=1 "…\NXBIN\run_journal.exe" M0_Reflect2.vb
Module M0Reflect2
    Private sb As StringBuilder = New StringBuilder()
    Private outPath As String = "C:\nx-vibe-journal-out\m0_reflect2.txt"

    Sub Main()
        Try
            Log("=== M0 反射探测 第二轮 ===")

            Log("--- CAMOperationCollection 方法 ---")
            DumpMethods(GetType(NXOpen.CAM.OperationCollection), "")

            Log("--- CavityMillingBuilder 成员（Depth/Feed/Stock/Cut）---")
            DumpMembers(GetType(NXOpen.CAM.CavityMillingBuilder), "Depth")
            DumpMembers(GetType(NXOpen.CAM.CavityMillingBuilder), "Feed")
            DumpMembers(GetType(NXOpen.CAM.CavityMillingBuilder), "Cut")

            Log("--- MillOrientGeomBuilder 成员（Mcs/Fixture/Clearance/Offset）---")
            DumpMembers(GetType(NXOpen.CAM.MillOrientGeomBuilder), "Mcs")
            DumpMembers(GetType(NXOpen.CAM.MillOrientGeomBuilder), "Fixture")
            DumpMembers(GetType(NXOpen.CAM.MillOrientGeomBuilder), "Clearance")
            DumpMembers(GetType(NXOpen.CAM.MillOrientGeomBuilder), "Offset")

            Log("--- MillMethodBuilder 成员 ---")
            DumpMembers(GetType(NXOpen.CAM.MillMethodBuilder), "")

            Log("--- MillingToolBuilder 成员（Dia/Flute/Corner/Num）---")
            DumpMembers(GetType(NXOpen.CAM.MillingToolBuilder), "Dia")
            DumpMembers(GetType(NXOpen.CAM.MillingToolBuilder), "Flute")
            DumpMembers(GetType(NXOpen.CAM.MillingToolBuilder), "Cor")
            DumpMembers(GetType(NXOpen.CAM.MillingToolBuilder), "Num")

            Log("--- NCGroup 成员（Member/Child/Name）---")
            DumpMembers(GetType(NXOpen.CAM.NCGroup), "Member")
            DumpMembers(GetType(NXOpen.CAM.NCGroup), "Child")
            DumpMembers(GetType(NXOpen.CAM.NCGroup), "Name")

            Log("--- InheritableDoubleBuilder 成员（未知点 1：继承态探测）---")
            DumpMembers(GetType(NXOpen.CAM.InheritableDoubleBuilder), "")

            Log("--- HoleDrillingBuilder 成员（Cycle/Depth/Retract/Point）---")
            DumpMembers(GetType(NXOpen.CAM.HoleDrillingBuilder), "Cycle")
            DumpMembers(GetType(NXOpen.CAM.HoleDrillingBuilder), "Depth")
            DumpMembers(GetType(NXOpen.CAM.HoleDrillingBuilder), "Retract")
            DumpMembers(GetType(NXOpen.CAM.HoleDrillingBuilder), "Point")

            Log("--- InheritableToolDepBuilder（cross_over_distance 对应）---")
            DumpMembers(GetType(NXOpen.CAM.InheritableToolDepBuilder), "")

            Log("--- Operation 通用成员（Name/Type）---")
            DumpMembers(GetType(NXOpen.CAM.Operation), "Name")
            DumpMembers(GetType(NXOpen.CAM.Operation), "Type")

            Log("--- FeedsBuilder 成员 ---")
            DumpMembers(GetType(NXOpen.CAM.FeedsBuilder), "")

            Log("=== 第二轮结束 ===")
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

    Private Sub Log(ByVal msg As String)
        sb.AppendLine(msg)
        Console.WriteLine(msg)
    End Sub
End Module
