Option Strict Off
Imports System
Imports System.IO
Imports System.Text
Imports NXOpen
Imports NXOpen.CAM

' M0 模板注册表探针：
' 1. GetTemplateTypes() 列当前注册的模板类型
' 2. 试 SpecifyConfiguration("cam_general.dat") 后再列/再建
' 输出 m0_templates.txt。
Module M0Templates
    Private sb As StringBuilder = New StringBuilder()
    Private outPath As String = "C:\nx-vibe-journal-out\m0_templates.txt"

    Sub Main()
        Try
            Dim s As Session = Session.GetSession()
            Log("=== M0 模板注册表探针 ===")
            Dim part1 As Part = s.Parts.NewDisplay("m0_tpl", Part.Units.Millimeters)
            s.Parts.SetWork(part1)
            If Not s.IsCamSessionInitialized Then s.CreateCamSession()

            Dim camSetup As CAMSetup = part1.CreateCamSetup("mill_planar")
            Log("CreateCamSetup: " & If(camSetup Is Nothing, "null", "ok"))
            ' 关键试验：切入制造应用模块，触发 CAM 网关完整初始化（对象模板注册表）
            Try
                s.ApplicationSwitchImmediate("UG_APP_MANUFACTURING")
                Log("ApplicationSwitchImmediate(UG_APP_MANUFACTURING) ✓")
            Catch ex As Exception
                Log("ApplicationSwitchImmediate 失败: " & ex.Message)
            End Try
            Dim cs As NXOpen.CAM.CAMSession = s.CAMSession
            Try
                Dim types() As String = cs.GetTemplateTypes()
                Log("当前注册模板类型数: " & types.Length.ToString())
                For Each t As String In types
                    Log("  T " & t)
                Next
            Catch ex As Exception
                Log("GetTemplateTypes 失败: " & ex.Message)
            End Try

            ' 试指定配置后再查
            Dim cfgDir As String = s.GetEnvironmentVariableValue("UGII_CAM_CONFIG_DIR")
            For Each cfg As String In New String() {"cam_general.dat", "cam_prismatic.dat"}
                Try
                    cs.SpecifyConfiguration(cfgDir & cfg)
                    Log("SpecifyConfiguration(" & cfg & ") ✓")
                    Try
                        Dim types2() As String = cs.GetTemplateTypes()
                        Log("  指定后注册模板类型数: " & types2.Length.ToString())
                        For Each t2 As String In types2
                            Log("    T " & t2)
                        Next
                    Catch ex As Exception
                        Log("  指定后 GetTemplateTypes 失败: " & ex.Message)
                    End Try
                    Exit For
                Catch ex As Exception
                    Log("SpecifyConfiguration(" & cfg & ") 失败: " & ex.Message)
                End Try
            Next


            ' ---- 组/工序创建试验（SpecifyConfiguration 之后）----
            Try
                Dim pr As NCGroup = camSetup.GetRoot(CAMSetup.View.ProgramOrder)
                Dim prog1 As NCGroup = camSetup.CAMGroupCollection.CreateProgram(pr, "PROGRAM", "", NXOpen.CAM.OperationCollection.UseDefaultName.False, "PROGRAM_P")
                Log("CreateProgram(PROGRAM) ✓")
                Dim mr As NCGroup = camSetup.GetRoot(CAMSetup.View.MachineMethod)
                Dim meth As NCGroup = camSetup.CAMGroupCollection.CreateMethod(mr, "MILL_METHOD", "", NXOpen.CAM.OperationCollection.UseDefaultName.False, "MILL_ROUGH_P")
                Log("CreateMethod(MILL_METHOD) ✓")
                Dim tr As NCGroup = camSetup.GetRoot(CAMSetup.View.MachineTool)
                Dim tool1 As NCGroup = camSetup.CAMGroupCollection.CreateTool(tr, "MILL", "", NXOpen.CAM.OperationCollection.UseDefaultName.False, "T1_D10_P")
                Log("CreateTool(MILL) ✓")
                Dim gr As NCGroup = camSetup.GetRoot(CAMSetup.View.Geometry)
                Dim geom As NCGroup = camSetup.CAMGroupCollection.CreateGeometry(gr, "MCS", "", NXOpen.CAM.OperationCollection.UseDefaultName.False, "MCS_1_P")
                Log("CreateGeometry(MCS) ✓")
                Dim op1 As NXOpen.CAM.Operation = camSetup.CAMOperationCollection.Create(prog1, meth, tool1, geom, "CAVITY_MILL", "", NXOpen.CAM.OperationCollection.UseDefaultName.False, "CAVITY_1_P")
                Log("Create(CAVITY_MILL) ✓ 父组=" & op1.ParentProgramOrder.Name)
            Catch ex As Exception
                Log("组/工序创建 ✗ " & ex.Message)
            End Try


            ' ---- AddTemplateType 手工注册对象模板 ----
            Dim tplPart As String = s.GetEnvironmentVariableValue("UGII_CAM_TEMPLATE_PART_METRIC_DIR") & "mill_planar.prt"
            Try
                cs.AddTemplateType(tplPart)
                Log("AddTemplateType(mill_planar.prt) ✓")
            Catch ex As Exception
                Log("AddTemplateType 失败: " & ex.Message)
            End Try
            ' 再试创建
            Try
                Dim pr As NCGroup = camSetup.GetRoot(CAMSetup.View.ProgramOrder)
                Dim prog1 As NCGroup = camSetup.CAMGroupCollection.CreateProgram(pr, "PROGRAM", "", NXOpen.CAM.OperationCollection.UseDefaultName.False, "PROGRAM_P2")
                Log("AddTemplateType 后 CreateProgram(PROGRAM) ✓")
            Catch ex As Exception
                Log("AddTemplateType 后 CreateProgram ✗ " & ex.Message)
            End Try

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
