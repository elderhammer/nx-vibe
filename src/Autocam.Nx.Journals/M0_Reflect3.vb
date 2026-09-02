Option Strict Off
Imports System
Imports System.IO
Imports System.Text
Imports System.Reflection
Imports NXOpen

' M0 反射第三轮：完整成员（含继承链）——前两轮 DeclaredOnly 漏了基类成员
' （CutParameters 在 MillOperationBuilder、Mcs 在 OrientGeomBuilder、Cycle 在
' HoleMachiningBuilder）。输出 m0_reflect3.txt。
Module M0Reflect3
    Private sb As StringBuilder = New StringBuilder()
    Private outPath As String = "C:\nx-vibe-journal-out\m0_reflect3.txt"

    Sub Main()
        Try
            Log("=== M0 反射第三轮（含继承）===")
            Dump("CavityMillingBuilder", GetType(NXOpen.CAM.CavityMillingBuilder), New String() {"DepthPerCut", "CutParameters", "FeedsBuilder", "NonCuttingBuilder", "Geometry", "CutLevel"})
            Dump("MillOrientGeomBuilder", GetType(NXOpen.CAM.MillOrientGeomBuilder), New String() {"Mcs", "FixtureOffset", "TransferClearance", "ToolAxis", "Rcs"})
            Dump("MillMethodBuilder", GetType(NXOpen.CAM.MillMethodBuilder), New String() {"CutParameters", "FeedsBuilder", "Stock", "PartStock", "FloorStock"})
            Dump("HoleDrillingBuilder", GetType(NXOpen.CAM.HoleDrillingBuilder), New String() {"Cycle", "Depth", "CuttingParameters", "Retract", "ControlPoint"})
            Dump("HoleMachiningBuilder", GetType(NXOpen.CAM.HoleMachiningBuilder), New String() {"Cycle", "Depth", "CuttingParameters", "HoleBossGeometry"})
            ' CutLevel 类型编译期不存在 → 运行时解析属性真实类型再 dump
            Dim clProp As PropertyInfo = GetType(NXOpen.CAM.CavityMillingBuilder).GetProperty("CutLevel")
            If clProp IsNot Nothing Then
                Dump("CutLevel 属性类型 " & clProp.PropertyType.Name, clProp.PropertyType, New String() {"DepthPerCut", "Type", "CommonDepth", "MaxDepth"})
            Else
                Log("-- CavityMillingBuilder 无 CutLevel 属性 --")
            End If
            Dump("FeedsBuilder", GetType(NXOpen.CAM.FeedsBuilder), New String() {"FeedCut", "FeedEngage", "FeedApproach", "FeedDeparture", "SpindleRpm", "Retract", "Coolant"})
            Dump("NcmClearanceBuilder", GetType(NXOpen.CAM.NcmClearanceBuilder), New String() {"ClearanceType", "SafeDistance", "Plane"})
            Dump("InheritableDoubleBuilder", GetType(NXOpen.CAM.InheritableDoubleBuilder), New String() {""})
            Dump("InheritableIntBuilder", GetType(NXOpen.CAM.InheritableIntBuilder), New String() {""})
            Dump("InheritableTextBuilder", GetType(NXOpen.CAM.InheritableTextBuilder), New String() {""})
            Dump("NCGroup", GetType(NXOpen.CAM.NCGroup), New String() {"GetMembers", "Name", "TypeName", "Subtype"})
            Dump("StepoverBuilder", GetType(NXOpen.CAM.StepoverBuilder), New String() {"Percent", "Value", "Mode"})
            Log("=== 第三轮结束 ===")
        Catch ex As Exception
            Log("FATAL: " & ex.ToString())
        End Try
        File.WriteAllText(outPath, sb.ToString())
    End Sub

    Private Sub Dump(ByVal label As String, ByVal t As Type, ByVal filters() As String)
        If t Is Nothing Then
            Log("-- " & label & "：(类型不存在)")
            Return
        End If
        Log("-- " & label & " --")
        For Each m As MemberInfo In t.GetMembers(BindingFlags.Public Or BindingFlags.Instance Or BindingFlags.Static)
            If m.MemberType <> MemberTypes.Property AndAlso m.MemberType <> MemberTypes.Method Then Continue For
            If m.Name.StartsWith("get_") OrElse m.Name.StartsWith("set_") Then Continue For
            If m.Name.StartsWith("add_") OrElse m.Name.StartsWith("remove_") Then Continue For
            Dim hit As Boolean = False
            For Each f As String In filters
                If f.Length = 0 OrElse m.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0 Then hit = True : Exit For
            Next
            If hit Then
                Log("  " & m.MemberType.ToString() & " " & m.Name & " : " & DeclType(m))
            End If
        Next
    End Sub

    Private Function DeclType(ByVal m As MemberInfo) As String
        Try
            If m.MemberType = MemberTypes.Property Then
                Return CType(m, PropertyInfo).PropertyType.Name
            End If
            If m.MemberType = MemberTypes.Method Then
                Dim mi As MethodInfo = CType(m, MethodInfo)
                Dim ps As String = ""
                For Each p As ParameterInfo In mi.GetParameters()
                    ps += p.ParameterType.Name & ","
                Next
                Return mi.ReturnType.Name & "(" & ps.TrimEnd(","c) & ")"
            End If
        Catch
        End Try
        Return ""
    End Function

    Private Sub Log(ByVal msg As String)
        sb.AppendLine(msg)
        Console.WriteLine(msg)
    End Sub
End Module
