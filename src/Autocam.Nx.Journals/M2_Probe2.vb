Option Strict Off
Imports System
Imports System.IO
Imports System.Text
Imports NXOpen
Imports NXOpen.CAM

' M2 模板探针 v2（GUI 会话）：Create* 键语义验证 + 反向 Builder 映射实测
'   ① 17 个 setup 族 × Operation 类 subtype 全枚举（找旧式键 FACE_MILLING/CAVITY_MILL 所在族）
'   ② (typeName=setup族, subtypeName=subtype) 的组链创建：Program→Method→Tool→Geometry
'   ③ mill_planar 全部 15 个 Operation subtype 逐个 CreateOperation + 回读 Builder 类名（反向映射表原料）
' 输出：C:\nx-vibe-journal-out\m2_probe2.txt
Module M2Probe2
    Private sb As StringBuilder = New StringBuilder()
    Private outPath As String = "C:\nx-vibe-journal-out\m2_probe2.txt"

    Sub Main()
        Try
            Dim s As Session = Session.GetSession()
            Log("=== M2 模板探针 v2 ===")

            Log("--- ① 各 setup 族 × Operation subtype ---")
            Dim types As String() = s.CAMSession.GetTemplateTypes()
            For Each t As String In types
                Dim ops As String() = s.CAMSession.GetTemplateSubtypes(t, CAMSession.ObjectSubtype.Operation)
                Log("  " & t & ": [" & String.Join(", ", ops) & "]")
            Next

            Log("--- ② (族, subtype) 组链创建 ---")
            Dim part As Part = s.Parts.NewDisplay("probe_tmpl2", Part.Units.Millimeters)
            s.Parts.SetWork(part)
            If Not s.IsCamSessionInitialized Then s.CreateCamSession()
            Dim cs As CAMSetup = part.CreateCamSetup("mill_planar")
            Log("CreateCamSetup(mill_planar): " & IIf(cs Is Nothing, "(null)", "ok"))

            Dim prog As NCGroup = Nothing
            Dim meth As NCGroup = Nothing
            Dim tool As NCGroup = Nothing
            Dim geom As NCGroup = Nothing
            TryCreate("CreateProgram(mill_planar/PROGRAM)", AddressOf cs.CAMGroupCollection.CreateProgram, cs.GetRoot(CAMSetup.View.ProgramOrder), "mill_planar", "PROGRAM", "P_PRG", prog)
            TryCreate("CreateMethod(mill_planar/MILL_METHOD)", AddressOf cs.CAMGroupCollection.CreateMethod, cs.GetRoot(CAMSetup.View.MachineMethod), "mill_planar", "MILL_METHOD", "P_METH", meth)
            TryCreate("CreateTool(mill_planar/MILL)", AddressOf cs.CAMGroupCollection.CreateTool, cs.GetRoot(CAMSetup.View.MachineTool), "mill_planar", "MILL", "P_TOOL", tool)
            TryCreate("CreateGeometry(mill_planar/MCS)", AddressOf cs.CAMGroupCollection.CreateGeometry, cs.GetRoot(CAMSetup.View.Geometry), "mill_planar", "MCS", "P_GEOM", geom)

            Log("--- ③ mill_planar 15 个 Operation subtype 创建 + Builder 反向映射 ---")
            Dim subs As String() = s.CAMSession.GetTemplateSubtypes("mill_planar", CAMSession.ObjectSubtype.Operation)
            For i As Integer = 0 To subs.Length - 1
                Dim subType As String = subs(i)
                Dim opName As String = "P_OP_" & i
                Try
                    Dim op As NXOpen.CAM.Operation = cs.CAMOperationCollection.Create(prog, meth, tool, geom, "mill_planar", subType, NXOpen.CAM.OperationCollection.UseDefaultName.False, opName)
                    Dim builder As OperationBuilder = Nothing
                    Dim builderName As String = "(CreateBuilder 失败)"
                    Try
                        builder = cs.CAMOperationCollection.CreateBuilder(op)
                        builderName = builder.GetType().Name
                    Catch ex As Exception
                        builderName = "✗ " & ex.Message
                    Finally
                        If builder IsNot Nothing Then
                            Try
                                builder.Destroy()
                            Catch
                            End Try
                        End If
                    End Try
                    Log("  CreateOperation(" & subType & ") ✓ -> builder=" & builderName)
                Catch ex As Exception
                    Log("  CreateOperation(" & subType & ") ✗ " & ex.Message)
                End Try
            Next

            Log("--- ④ 旧式键对照（预期 ✗，证明口径差异）---")
            TryCreateOp2(cs, prog, meth, tool, geom, "mill_planar", "FACE_MILLING", "P_OP_LEGACY")

            Log("=== 结束 ===")
        Catch ex As Exception
            Log("FATAL: " & ex.ToString())
        End Try
        File.WriteAllText(outPath, sb.ToString())
    End Sub

    Private Delegate Function CreateGroupFn(parent As NCGroup, typeName As String, subtypeName As String, useDefault As NXOpen.CAM.OperationCollection.UseDefaultName, name As String) As NCGroup

    Private Sub TryCreate(desc As String, fn As CreateGroupFn, parent As NCGroup, typeName As String, subtypeName As String, name As String, ByRef outGroup As NCGroup)
        Try
            outGroup = fn(parent, typeName, subtypeName, NXOpen.CAM.OperationCollection.UseDefaultName.False, name)
            Log("  " & desc & " ✓ -> " & IIf(outGroup Is Nothing, "(null)", outGroup.Name))
        Catch ex As Exception
            Log("  " & desc & " ✗ " & ex.Message)
        End Try
    End Sub

    Private Sub TryCreateOp2(cs As CAMSetup, prog As NCGroup, meth As NCGroup, tool As NCGroup, geom As NCGroup, typeName As String, subtypeName As String, opName As String)
        Try
            Dim op As NXOpen.CAM.Operation = cs.CAMOperationCollection.Create(prog, meth, tool, geom, typeName, subtypeName, NXOpen.CAM.OperationCollection.UseDefaultName.False, opName)
            Log("  CreateOperation(" & typeName & "/" & subtypeName & ") ✓ (意外) -> " & op.Name)
        Catch ex As Exception
            Log("  CreateOperation(" & typeName & "/" & subtypeName & ") ✗ " & ex.Message & "（预期——口径差异实证）")
        End Try
    End Sub

    Private Sub Log(ByVal msg As String)
        sb.AppendLine(msg)
        Console.WriteLine(msg)
    End Sub
End Module
