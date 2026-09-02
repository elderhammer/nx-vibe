Option Strict Off
Imports System
Imports System.IO
Imports System.Text
Imports NXOpen
Imports NXOpen.CAM

' M2 模板注册表探针（GUI 会话跑）：回答三条——
'   ① GetTemplateTypes() 全量注册类型键
'   ② GetTemplateSubtypes("mill_planar", 各 ObjectSubtype) 采样
'   ③ 新 part + CreateCamSetup("mill_planar") 后，各视图组/工序 Create 的 (type, subtype) 候选试验
' 编译：vbc /target:exe /out:C:\nx-vibe-journal-out\M2_ProbeTemplates.exe /r:System.dll /r:System.Core.dll /r:"…\NXBIN\managed\NXOpen.dll" M2_ProbeTemplates.vb
' 输出：C:\nx-vibe-journal-out\m2_probe_templates.txt
Module M2ProbeTemplates
    Private sb As StringBuilder = New StringBuilder()
    Private outPath As String = "C:\nx-vibe-journal-out\m2_probe_templates.txt"

    Sub Main()
        Try
            Dim s As Session = Session.GetSession()
            Log("=== M2 模板注册表/类型键探针（GUI 会话）===")

            Log("--- ① CAMSession.GetTemplateTypes() ---")
            For Each t As String In s.CAMSession.GetTemplateTypes()
                Log("  T " & t)
            Next

            Log("--- ② GetTemplateSubtypes('mill_planar', 各 ObjectSubtype) ---")
            For Each osName As String In [Enum].GetNames(GetType(CAMSession.ObjectSubtype))
                Try
                    Dim os As CAMSession.ObjectSubtype = CType([Enum].Parse(GetType(CAMSession.ObjectSubtype), osName), CAMSession.ObjectSubtype)
                    Dim subs As String() = s.CAMSession.GetTemplateSubtypes("mill_planar", os)
                    Dim txt As String = ""
                    If subs IsNot Nothing Then txt = String.Join(", ", subs)
                    Log("  mill_planar / " & osName & " (" & CInt(os) & "): [" & txt & "]")
                Catch ex As Exception
                    Log("  mill_planar / " & osName & ": ✗ " & ex.Message)
                End Try
            Next

            Log("--- ③ 新建 part + setup 后 Create 候选试验 ---")
            Dim part As Part = s.Parts.NewDisplay("probe_tmpl", Part.Units.Millimeters)
            s.Parts.SetWork(part)
            If Not s.IsCamSessionInitialized Then s.CreateCamSession()
            Dim cs As CAMSetup = part.CreateCamSetup("mill_planar")
            Log("CreateCamSetup(mill_planar): " & IIf(cs Is Nothing, "(null)", "ok"))

            TryGroup(cs, "CreateProgram", AddressOf TryCreateProgram, {"PROGRAM|", "PROGRAM|PROGRAM", "NC_PROGRAM|", "PROGRAM_ORDER|", "MILL_PROGRAM|"})
            TryGroup(cs, "CreateMethod", AddressOf TryCreateMethod, {"MILL_METHOD|", "MILL_METHOD|MILL_ROUGH", "MILL_ROUGH|", "MILL_ROUGH|MILL_ROUGH", "METHOD|", "DRILL_METHOD|", "DRILL_METHOD|DRILL_ROUGH"})
            TryGroup(cs, "CreateTool", AddressOf TryCreateTool, {"MILL|", "MILL|5_PARAMETER", "MILL|MILL_5_PARAMETER", "DRILL|", "DRILL|DRILL_5_PARAMETER", "CHAMFER_MILL|", "BALL_MILL|"})
            TryGroup(cs, "CreateGeometry", AddressOf TryCreateGeometry, {"MCS|", "MCS|MCS_MAIN", "MCS_MAIN|", "GEOMETRY|"})

            Log("--- ③b 模板现成组建工序 Create ---")
            Try
                Dim prog As NCGroup = FindChild(cs, CAMSetup.View.ProgramOrder, "PROGRAM")
                Dim meth As NCGroup = FindChild(cs, CAMSetup.View.MachineMethod, "MILL_ROUGH")
                Dim tool As NCGroup = FindChild(cs, CAMSetup.View.MachineTool, "MILL")
                Dim geom As NCGroup = FindChild(cs, CAMSetup.View.Geometry, "MCS_MAIN")
                Log("现成组: " & IIf(prog Is Nothing, "(null)", prog.Name) & " / " & IIf(meth Is Nothing, "(null)", meth.Name) & " / " & IIf(tool Is Nothing, "(null)", tool.Name) & " / " & IIf(geom Is Nothing, "(null)", geom.Name))
                TryCreateOp(cs, prog, meth, tool, geom, "CAVITY_MILL", "")
                TryCreateOp(cs, prog, meth, tool, geom, "FACE_MILLING", "")
                TryCreateOp(cs, prog, meth, tool, geom, "PLANAR_MILL", "")
                TryCreateOp(cs, prog, meth, tool, geom, "GROOVE_MILL", "")
                TryCreateOp(cs, prog, meth, tool, geom, "DOCUMENTATION", "")
            Catch ex As Exception
                Log("③b 准备现成组失败: " & ex.Message)
            End Try

            Log("=== 结束 ===")
        Catch ex As Exception
            Log("FATAL: " & ex.ToString())
        End Try
        File.WriteAllText(outPath, sb.ToString())
    End Sub

    Private Sub TryGroup(cs As CAMSetup, title As String, act As Action(Of CAMSetup, String, String), pairs As String())
        Log("--- " & title & " ---")
        For Each pair As String In pairs
            Dim p As String() = pair.Split("|"c)
            Try
                act(cs, p(0), p(1))
                Log("  [" & pair & "] ✓")
            Catch ex As Exception
                Log("  [" & pair & "] ✗ " & ex.Message)
            End Try
        Next
    End Sub

    Private Sub TryCreateProgram(cs As CAMSetup, typeName As String, subtypeName As String)
        Dim g As NCGroup = cs.CAMGroupCollection.CreateProgram(cs.GetRoot(CAMSetup.View.ProgramOrder), typeName, subtypeName, NXOpen.CAM.OperationCollection.UseDefaultName.False, "PROBE_P_" & typeName.Replace(" ", "_") & "_" & subtypeName.Replace(" ", "_"))
    End Sub

    Private Sub TryCreateMethod(cs As CAMSetup, typeName As String, subtypeName As String)
        Dim g As NCGroup = cs.CAMGroupCollection.CreateMethod(cs.GetRoot(CAMSetup.View.MachineMethod), typeName, subtypeName, NXOpen.CAM.OperationCollection.UseDefaultName.False, "PROBE_M_" & typeName.Replace(" ", "_") & "_" & subtypeName.Replace(" ", "_"))
    End Sub

    Private Sub TryCreateTool(cs As CAMSetup, typeName As String, subtypeName As String)
        Dim g As NCGroup = cs.CAMGroupCollection.CreateTool(cs.GetRoot(CAMSetup.View.MachineTool), typeName, subtypeName, NXOpen.CAM.OperationCollection.UseDefaultName.False, "PROBE_T_" & typeName.Replace(" ", "_") & "_" & subtypeName.Replace(" ", "_"))
    End Sub

    Private Sub TryCreateGeometry(cs As CAMSetup, typeName As String, subtypeName As String)
        Dim g As NCGroup = cs.CAMGroupCollection.CreateGeometry(cs.GetRoot(CAMSetup.View.Geometry), typeName, subtypeName, NXOpen.CAM.OperationCollection.UseDefaultName.False, "PROBE_G_" & typeName.Replace(" ", "_") & "_" & subtypeName.Replace(" ", "_"))
    End Sub

    Private Function FindChild(cs As CAMSetup, view As CAMSetup.View, name As String) As NCGroup
        For Each m As CAMObject In cs.GetRoot(view).GetMembers()
            Dim g As NCGroup = TryCast(m, NCGroup)
            If g IsNot Nothing AndAlso g.Name = name Then Return g
        Next
        Return Nothing
    End Function

    Private Sub TryCreateOp(cs As CAMSetup, prog As NCGroup, meth As NCGroup, tool As NCGroup, geom As NCGroup, typeName As String, subtypeName As String)
        Try
            Dim op As NXOpen.CAM.Operation = cs.CAMOperationCollection.Create(prog, meth, tool, geom, typeName, subtypeName, NXOpen.CAM.OperationCollection.UseDefaultName.False, "PROBE_OP_" & typeName.Replace(" ", "_"))
            Log("  CreateOperation [" & typeName & "/" & subtypeName & "] ✓ -> " & op.Name)
        Catch ex As Exception
            Log("  CreateOperation [" & typeName & "/" & subtypeName & "] ✗ " & ex.Message)
        End Try
    End Sub

    Private Sub Log(ByVal msg As String)
        sb.AppendLine(msg)
        Console.WriteLine(msg)
    End Sub
End Module
