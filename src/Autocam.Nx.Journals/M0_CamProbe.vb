Option Strict Off
Imports System
Imports System.IO
Imports System.Text
Imports NXOpen
Imports NXOpen.CAM

' M0 运行时验证 v2：在模板零件的 CAMSetup 里实测 5 个未知点。
' 1. 继承态：方法组设 PartStock=0.5，工序不设 → CavityMillingBuilder 读 Value + InheritanceStatus
' 2. 嵌套 Program 组：CreateProgram(parent=PROGRAM)
' 3. 四父组回读：op.ParentProgramOrder/ParentMachineMethod/ParentMachineTool/ParentGeometry
' 4. 刀具 Builder 分派：MillToolBuilder / DrillStdToolBuilder
' 5. 操作集合枚举序稳定性
' 输出 m0_camprobe.txt。运行：UGII_BATCH_MODE=1 "…\NXBIN\run_journal.exe" M0_CamProbe.vb
Module M0CamProbe
    Private sb As StringBuilder = New StringBuilder()
    Private outPath As String = "C:\nx-vibe-journal-out\m0_camprobe.txt"

    Sub Main()
        Try
            Dim s As Session = Session.GetSession()
            Log("=== M0 运行时验证 v2 ===")
            Probe(s)
        Catch ex As Exception
            Log("FATAL: " & ex.ToString())
        End Try
        File.WriteAllText(outPath, sb.ToString())
    End Sub

    Private Sub Probe(ByVal s As Session)
        ' 新链路（M0_WorkPartProbe 已验证）：NewDisplay → SetWork → CreateCamSession → CreateCamSetup
        Dim part1 As Part = s.Parts.NewDisplay("m0_probe", Part.Units.Millimeters)
        s.Parts.SetWork(part1)
        If Not s.IsCamSessionInitialized Then
            s.CreateCamSession()
        End If
        Dim camSetup As CAMSetup = part1.CreateCamSetup("mill_planar")
        If camSetup Is Nothing Then
            Log("FATAL: CreateCamSetup 返回 null")
            Return
        End If
        Log("CAMSetup 就绪（CreateCamSetup 链路）")

        ' ---- 组创建（未知点 2：嵌套 Program）----
        Dim progRoot As NCGroup = Nothing
        Dim prog1 As NCGroup = Nothing
        Dim methodP As NCGroup = Nothing
        Dim toolMill As NCGroup = Nothing
        Dim toolDrill As NCGroup = Nothing
        Dim geomP As NCGroup = Nothing
        Try
            Dim pr As NCGroup = camSetup.GetRoot(CAMSetup.View.ProgramOrder)
            progRoot = camSetup.CAMGroupCollection.CreateProgram(pr, "PROGRAM", "", NXOpen.CAM.OperationCollection.UseDefaultName.False, "PROGRAM_P")
            Log("P2 ok: 根下建 PROGRAM_P（type=PROGRAM）")
            prog1 = camSetup.CAMGroupCollection.CreateProgram(progRoot, "PROGRAM", "", NXOpen.CAM.OperationCollection.UseDefaultName.False, "PROGRAM_1_P")
            Log("P2 ok: PROGRAM_P 下嵌套建 PROGRAM_1_P（嵌套支持 ✓）")
            Dim members() As CAMObject = progRoot.GetMembers()
            Log("P2 GetMembers(PROGRAM_P) 子组: " & JoinNames(members))
        Catch ex As Exception
            Log("FAIL P2 组树: " & ex.Message)
        End Try
        Try
            Dim mr As NCGroup = camSetup.GetRoot(CAMSetup.View.MachineMethod)
            methodP = camSetup.CAMGroupCollection.CreateMethod(mr, "MILL_METHOD", "", NXOpen.CAM.OperationCollection.UseDefaultName.False, "MILL_ROUGH_P")
            Log("组 ok: 方法组 MILL_ROUGH_P（type=MILL_METHOD）")
        Catch ex As Exception
            Log("FAIL 方法组: " & ex.Message)
        End Try
        Try
            Dim tr As NCGroup = camSetup.GetRoot(CAMSetup.View.MachineTool)
            toolMill = camSetup.CAMGroupCollection.CreateTool(tr, "MILL", "", NXOpen.CAM.OperationCollection.UseDefaultName.False, "T1_D10_P")
            Log("组 ok: 刀具组 T1_D10_P（type=MILL）")
            toolDrill = camSetup.CAMGroupCollection.CreateTool(tr, "DRILL", "", NXOpen.CAM.OperationCollection.UseDefaultName.False, "T2_D6.8_P")
            Log("组 ok: 刀具组 T2_D6.8_P（type=DRILL）")
        Catch ex As Exception
            Log("FAIL 刀具组: " & ex.Message)
        End Try
        Try
            Dim gr As NCGroup = camSetup.GetRoot(CAMSetup.View.Geometry)
            geomP = camSetup.CAMGroupCollection.CreateGeometry(gr, "MCS", "", NXOpen.CAM.OperationCollection.UseDefaultName.False, "MCS_1_P")
            Log("组 ok: 几何组 MCS_1_P（type=MCS）")
        Catch ex As Exception
            Log("FAIL 几何组: " & ex.Message)
        End Try

        ' ---- 方法组设 PartStock=0.5（未知点 1 铺垫）----
        If methodP IsNot Nothing Then
            Try
                Dim mb As MillMethodBuilder = camSetup.CAMGroupCollection.CreateMillMethodBuilder(methodP)
                mb.CutParameters.PartStock.Value = 0.5
                mb.Commit()
                mb.Destroy()
                Log("P1 铺垫 ok: 方法组 PartStock=0.5 已 Commit")
            Catch ex As Exception
                Log("FAIL P1 铺垫: " & ex.Message)
            End Try
        End If

        ' ---- 刀具 Builder（未知点 4）----
        If toolMill IsNot Nothing Then
            Try
                Dim mb As MillingToolBuilder = camSetup.CAMGroupCollection.CreateMillToolBuilder(toolMill)
                mb.TlDiameterBuilder.Value = 10.0
                mb.TlNumFlutesBuilder.Value = 4
                mb.Commit()
                mb.Destroy()
                Log("P4 ok: MillToolBuilder 直径 10 / 刃数 4")
            Catch ex As Exception
                Log("FAIL P4 MillToolBuilder: " & ex.Message)
            End Try
        End If
        If toolDrill IsNot Nothing Then
            Try
                Dim db As DrillStdToolBuilder = camSetup.CAMGroupCollection.CreateDrillStdToolBuilder(toolDrill)
                db.TlDiameterBuilder.Value = 6.8
                db.Commit()
                db.Destroy()
                Log("P4 ok: DrillStdToolBuilder 直径 6.8")
            Catch ex As Exception
                Log("FAIL P4 DrillStdToolBuilder: " & ex.Message)
            End Try
        End If

        ' ---- 建工序（CAVITY_MILL + DRILL）----
        Dim op1 As NXOpen.CAM.Operation = Nothing
        Dim op2 As NXOpen.CAM.Operation = Nothing
        Try
            op1 = camSetup.CAMOperationCollection.Create(prog1, methodP, toolMill, geomP, "CAVITY_MILL", "", NXOpen.CAM.OperationCollection.UseDefaultName.False, "CAVITY_1_P")
            Log("工序 ok: CAVITY_MILL 创建")
        Catch ex As Exception
            Log("FAIL CAVITY_MILL 创建: " & ex.ToString())
        End Try
        Try
            op2 = camSetup.CAMOperationCollection.Create(prog1, methodP, toolDrill, geomP, "DRILL", "", NXOpen.CAM.OperationCollection.UseDefaultName.False, "DRILL_1_P")
            Log("工序 ok: DRILL 创建")
        Catch ex As Exception
            Log("FAIL DRILL 创建: " & ex.ToString())
        End Try

        ' ---- 未知点 3：四父组回读 ----
        If op1 IsNot Nothing Then
            Try
                Log("P3 ok: 父组 = " & op1.ParentProgramOrder.Name & " / " & op1.ParentMachineMethod.Name _
                    & " / " & op1.ParentMachineTool.Name & " / " & op1.ParentGeometry.Name)
            Catch ex As Exception
                Log("FAIL P3: " & ex.Message)
            End Try
        End If

        ' ---- 未知点 1：继承态读取 ----
        If op1 IsNot Nothing Then
            Try
                Dim cb As CavityMillingBuilder = camSetup.CAMOperationCollection.CreateCavityMillingBuilder(op1)
                Dim psb As InheritableDoubleBuilder = cb.CutParameters.PartStock
                Dim v As Double = 0.0
                Try
                    v = psb.Value
                    Log("P1 Value 读取: " & v.ToString() & "（0.5=生效值直读）")
                Catch ex As Exception
                    Log("P1 Value 读取异常: " & ex.Message)
                End Try
                Log("P1 InheritanceStatus: " & psb.InheritanceStatus.ToString() & "（True=继承态可探测）")
                Dim dp As InheritableDoubleBuilder = cb.DepthPerCut
                Log("P1 DepthPerCut 存在: Value=" & dp.Value.ToString() & " Inherited=" & dp.InheritanceStatus.ToString())
                cb.Destroy()
            Catch ex As Exception
                Log("FAIL P1: " & ex.ToString())
            End Try
        End If

        ' ---- 类型名成员探测（M1 需要 operation 的 typeName）----
        Try
            Dim t As Type = GetType(NXOpen.CAM.Operation)
            Log("Operation 类型名成员: " & String.Join(", ", Array.ConvertAll(t.GetMembers(), Function(m) m.Name)))
        Catch ex As Exception
            Log("类型名探测失败: " & ex.Message)
        End Try

        ' ---- 未知点 5：操作集合枚举序 ----
        Try
            Dim order1 As String = ""
            For Each o As NXOpen.CAM.Operation In camSetup.CAMOperationCollection
                order1 += o.Name & ","
            Next
            Dim order2 As String = ""
            For Each o As NXOpen.CAM.Operation In camSetup.CAMOperationCollection
                order2 += o.Name & ","
            Next
            If order1 = order2 Then
                Log("P5 ok: 枚举序稳定（" & order1 & "）")
            Else
                Log("P5 不稳定!: " & order1 & " vs " & order2)
            End If
        Catch ex As Exception
            Log("FAIL P5: " & ex.Message)
        End Try

        Log("=== M0 结束 ===")
    End Sub

    Private Function JoinNames(ByVal objs() As CAMObject) As String
        Dim r As String = ""
        For Each o As CAMObject In objs
            r += o.Name & ","
        Next
        Return r
    End Function

    Private Sub Log(ByVal msg As String)
        sb.AppendLine(msg)
        Console.WriteLine(msg)
    End Sub
End Module
