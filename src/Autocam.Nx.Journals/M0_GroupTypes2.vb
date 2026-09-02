Option Strict Off
Imports System
Imports System.IO
Imports System.Text
Imports System.Reflection
Imports NXOpen
Imports NXOpen.CAM

' M0 组类型探针 v2：
' 1. NCGroupBuilder 成员（能否绕过模板注册表建组）
' 2. CreateProgram 试 "NC_PROGRAM"
' 3. 用模板现成组直接建工序（PROGRAM+MILL_ROUGH+MILL+MCS_MAIN）
' 输出 m0_grouptypes2.txt。
Module M0GroupTypes2
    Private sb As StringBuilder = New StringBuilder()
    Private outPath As String = "C:\nx-vibe-journal-out\m0_grouptypes2.txt"

    Sub Main()
        Try
            Dim s As Session = Session.GetSession()
            Log("=== M0 组类型探针 v2 ===")

            Log("--- NCGroupBuilder 成员 ---")
            For Each m As MemberInfo In GetType(NCGroupBuilder).GetMembers(BindingFlags.Public Or BindingFlags.Instance Or BindingFlags.Static Or BindingFlags.DeclaredOnly)
                If m.MemberType = MemberTypes.Property AndAlso Not m.Name.StartsWith("get_") AndAlso Not m.Name.StartsWith("set_") Then
                    Dim pt As PropertyInfo = CType(m, PropertyInfo)
                    Log("  P " & m.Name & " : " & pt.PropertyType.Name)
                End If
            Next
            For Each mi As MethodInfo In GetType(NCGroupBuilder).GetMethods(BindingFlags.Public Or BindingFlags.Instance Or BindingFlags.DeclaredOnly)
                If mi.Name = "Commit" OrElse mi.Name = "Destroy" Then
                    Log("  M " & mi.ReturnType.Name & " " & mi.Name)
                End If
            Next

            ' 打开模板零件（真实 CAMSetup）
            Dim dst As String = "C:\nx-vibe-journal-out\parts\m0_template.prt"
            Dim tplDir As String = s.GetEnvironmentVariableValue("UGII_CAM_TEMPLATE_PART_ENGLISH_DIR")
            If Not File.Exists(dst) Then File.Copy(tplDir & "mill_planar.prt", dst)
            Dim loadStatus As PartLoadStatus = Nothing
            Dim part1 As Part = s.Parts.OpenDisplay(dst, loadStatus)
            s.Parts.SetWork(part1)
            If Not s.IsCamSessionInitialized Then s.CreateCamSession()
            Dim camSetup As CAMSetup = part1.CAMSetup

            Log("--- CreateProgram 试 NC_PROGRAM ---")
            Try
                Dim pr As NCGroup = camSetup.GetRoot(CAMSetup.View.ProgramOrder)
                Dim g1 As NCGroup = camSetup.CAMGroupCollection.CreateProgram(pr, "NC_PROGRAM", "", NXOpen.CAM.OperationCollection.UseDefaultName.False, "PROG_NCP")
                Log("NC_PROGRAM ✓")
            Catch ex As Exception
                Log("NC_PROGRAM ✗ " & ex.Message)
            End Try

            Log("--- 用模板现成组建工序 ---")
            Dim prRoot As NCGroup = camSetup.GetRoot(CAMSetup.View.ProgramOrder)
            Dim progGroup As NCGroup = FindGroup(prRoot, "PROGRAM")
            Dim methodGroup As NCGroup = FindGroup(camSetup.GetRoot(CAMSetup.View.MachineMethod), "MILL_ROUGH")
            Dim toolGroup As NCGroup = FindGroup(camSetup.GetRoot(CAMSetup.View.MachineTool), "MILL")
            Dim geomGroup As NCGroup = FindGroup(camSetup.GetRoot(CAMSetup.View.Geometry), "MCS_MAIN")
            Log("现成组: " & If(progGroup Is Nothing, "(null)", progGroup.Name) & " / " & If(methodGroup Is Nothing, "(null)", methodGroup.Name) _
                & " / " & If(toolGroup Is Nothing, "(null)", toolGroup.Name) & " / " & If(geomGroup Is Nothing, "(null)", geomGroup.Name))
            Try
                Dim op1 As NXOpen.CAM.Operation = camSetup.CAMOperationCollection.Create(progGroup, methodGroup, toolGroup, geomGroup, "CAVITY_MILL", "", NXOpen.CAM.OperationCollection.UseDefaultName.False, "CAVITY_TEST")
                Log("CAVITY_MILL ✓ 父组: " & op1.ParentProgramOrder.Name)
                Try
                    Dim cb As CavityMillingBuilder = camSetup.CAMOperationCollection.CreateCavityMillingBuilder(op1)
                    cb.DepthPerCut.Value = 2.0
                    cb.Commit()
                    cb.Destroy()
                    Log("CavityMillingBuilder DepthPerCut=2.0 Commit ✓")
                Catch ex As Exception
                    Log("CavityMillingBuilder ✗ " & ex.Message)
                End Try
            Catch ex As Exception
                Log("CAVITY_MILL ✗ " & ex.ToString())
            End Try
            Try
                Dim op2 As NXOpen.CAM.Operation = camSetup.CAMOperationCollection.Create(progGroup, methodGroup, toolGroup, geomGroup, "DRILL", "", NXOpen.CAM.OperationCollection.UseDefaultName.False, "DRILL_TEST")
                Log("DRILL ✓ 父组: " & op2.ParentProgramOrder.Name)
            Catch ex As Exception
                Log("DRILL ✗ " & ex.ToString())
            End Try

            Log("=== 结束 ===")
        Catch ex As Exception
            Log("FATAL: " & ex.ToString())
        End Try
        File.WriteAllText(outPath, sb.ToString())
    End Sub

    Private Function FindGroup(ByVal root As NCGroup, ByVal name As String) As NCGroup
        If root Is Nothing Then Return Nothing
        If root.Name = name Then Return root
        For Each m As CAMObject In root.GetMembers()
            Dim subg As NCGroup = TryCast(m, NCGroup)
            If subg IsNot Nothing Then
                Dim r As NCGroup = FindGroup(subg, name)
                If r IsNot Nothing Then Return r
            End If
        Next
        Return Nothing
    End Function

    Private Sub Log(ByVal msg As String)
        sb.AppendLine(msg)
        Console.WriteLine(msg)
    End Sub
End Module
