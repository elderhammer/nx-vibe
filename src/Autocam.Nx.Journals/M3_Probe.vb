Option Strict Off
Imports System
Imports System.IO
Imports System.Text
Imports System.Reflection
Imports NXOpen
Imports NXOpen.CAM

' M3 保真度探针（GUI 会话）：一次回答 W0/W1/W3 三问
'   A. 两零件（m1 原件 / rebuild_part）MachineTool 树递归 dump + 每组的可读性
'      （Mill/Drill builder 尝试）→ 定位 T-002/T-003 真实身份与 T-005 幻影来源
'   B. m1 全部 op：名称 + GetNameOfType + ParentMachineTool/Method 名 + 属性样本
'      → 查 op 是否携带真实模板键（W0）
'   C. rebuild_part 上 POCKETING op 的 Builder：NxParamPaths.Operation 全 23 路径逐个
'      探测落点 → VolumeBased25D 写路径扩展表原料（W1）
' 输出：C:\nx-vibe-journal-out\m3_probe.txt
Module M3Probe
    Private sb As StringBuilder = New StringBuilder()
    Private outPath As String = "C:\nx-vibe-journal-out\m3_probe.txt"

    Sub Main()
        Try
            Dim s As Session = Session.GetSession()
            Log("=== M3 保真度探针 ===")

            Dim m1 As Part = OpenOrFind(s, "C:\nx-vibe-journal-out\parts\m1_template_metric.prt")
            Dim rp As Part = OpenOrFind(s, "C:\nx-vibe-journal-out\parts\rebuild_part.prt")

            Log("--- A. 刀具树（m1 vs rebuild_part）---")
            DumpToolTree(m1, "m1")
            DumpToolTree(rp, "rebuild_part")

            Log("--- B. m1 工序键探针 ---")
            If m1 IsNot Nothing Then
                For Each op As NXOpen.CAM.Operation In m1.CAMSetup.CAMOperationCollection.ToArray()
                    Dim toolName As String = ""
                    Dim methodName As String = ""
                    Try
                        toolName = op.ParentMachineTool.Name
                    Catch
                    End Try
                    Try
                        methodName = op.ParentMachineMethod.Name
                    Catch
                    End Try
                    Dim typeLabel As String = ""
                    Try
                        typeLabel = op.GetNameOfType()
                    Catch
                    End Try
                    Dim attrInfo As String = "(attrs API 未探)"
                    Try
                        Dim m As MethodInfo = GetType(NXOpen.CAM.Operation).GetMethod("GetUserAttributes", New Type() {})
                        If m IsNot Nothing Then
                            Dim arr As Object = m.Invoke(op, Nothing)
                            Dim titles As New System.Collections.Generic.List(Of String)
                            For Each a As Object In CType(arr, System.Collections.IEnumerable)
                                Dim tProp As PropertyInfo = a.GetType().GetProperty("Title")
                                If tProp IsNot Nothing Then
                                    titles.Add(tProp.GetValue(a, Nothing).ToString())
                                End If
                            Next
                            attrInfo = titles.Count.ToString() & " attrs: " & String.Join(",", titles.ToArray())
                        Else
                            attrInfo = "(无 GetUserAttributes() 无参重载)"
                        End If
                    Catch ex As Exception
                        attrInfo = "(attrs 读取失败: " & ex.Message & ")"
                    End Try
                    Log("  op " & op.Name & " | GetNameOfType=" & typeLabel & " | tool=" & toolName & " | method=" & methodName & " | " & attrInfo)
                Next
                Log("  --- Operation 类型含 Template/TypeName/NameOfType 的成员 ---")
                For Each mi As MethodInfo In GetType(NXOpen.CAM.Operation).GetMethods()
                    If mi.Name.IndexOf("Template", StringComparison.OrdinalIgnoreCase) >= 0 OrElse mi.Name.IndexOf("TypeName", StringComparison.OrdinalIgnoreCase) >= 0 OrElse mi.Name.IndexOf("NameOfType", StringComparison.OrdinalIgnoreCase) >= 0 Then
                        Log("  M " & mi.ReturnType.Name & " " & mi.Name)
                    End If
                Next
                For Each pi As PropertyInfo In GetType(NXOpen.CAM.Operation).GetProperties()
                    If pi.Name.IndexOf("Template", StringComparison.OrdinalIgnoreCase) >= 0 OrElse pi.Name.IndexOf("Type", StringComparison.OrdinalIgnoreCase) >= 0 Then
                        Log("  P " & pi.PropertyType.Name & " " & pi.Name)
                    End If
                Next
            End If

            Log("--- C. 各 Builder 类型 × 路径探测（rebuild_part 全部 op，按 builder 去重）---")
            If rp IsNot Nothing Then
                Dim paths As String() = New String() {
                    "DepthPerCut", "CutParameters.FloorStock", "CutParameters.WallStock",
                    "CutParameters.PartStock", "CutParameters.Stepover", "CutParameters.CutOrder",
                    "CutParameters.CutDirection", "CutParameters.FinishPasses", "CutParameters.MultiDepthCut",
                    "CutParameters.BoundaryInTol", "CutParameters.BoundaryOutTol",
                    "FeedsBuilder.SpindleRpmBuilder", "FeedsBuilder.SurfaceSpeedBuilder",
                    "FeedsBuilder.FeedCutBuilder", "FeedsBuilder.FeedApproachBuilder",
                    "FeedsBuilder.FeedEngageBuilder", "FeedsBuilder.FeedDepartureBuilder",
                    "FeedsBuilder.RetractSpeed", "CuttingParameters.BottomStock",
                    "CuttingParameters.BottomClearance", "CuttingParameters.MinimalClearance",
                    "CuttingParameters.TopOffset", "ControlPointOffset", "RetractOutputMode"}
                Dim seen As New System.Collections.Generic.HashSet(Of String)
                For Each o As NXOpen.CAM.Operation In rp.CAMSetup.CAMOperationCollection.ToArray()
                    Dim b As OperationBuilder = Nothing
                    Try
                        b = rp.CAMSetup.CAMOperationCollection.CreateBuilder(o)
                        Dim bn As String = b.GetType().Name
                        If Not seen.Contains(bn) Then
                            seen.Add(bn)
                            Log("  == " & bn & " （例 op: " & o.Name & "）==")
                            Dim topNames As New System.Collections.Generic.List(Of String)
                            For Each pi As PropertyInfo In b.GetType().GetProperties()
                                topNames.Add(pi.Name)
                            Next
                            Log("  顶层属性: " & String.Join(", ", topNames.ToArray()))
                            Dim cp As Object = WalkPath(b, "CutParameters")
                            If cp IsNot Nothing Then
                                Dim cpNames As New System.Collections.Generic.List(Of String)
                                For Each pi As PropertyInfo In cp.GetType().GetProperties()
                                    cpNames.Add(pi.Name)
                                Next
                                Log("  CutParameters(" & cp.GetType().Name & ") 属性: " & String.Join(", ", cpNames.ToArray()))
                            End If
                            For Each p As String In paths
                                Dim leaf As Object = WalkPath(b, p)
                                If leaf Is Nothing Then
                                    Log("  " & p & " -> ✗")
                                Else
                                    Log("  " & p & " -> " & leaf.GetType().Name)
                                End If
                            Next
                        End If
                    Catch ex As Exception
                        Log("  op " & o.Name & " CreateBuilder 失败: " & ex.Message)
                    Finally
                        If b IsNot Nothing Then
                            Try
                                b.Destroy()
                            Catch
                            End Try
                        End If
                    End Try
                Next
            End If

            Log("--- D. 写入语义探针：设 0.0 后是否生效（InheritableDoubleBuilder / 继承态）---")
            If rp IsNot Nothing Then
                Dim targets As String() = New String() {"FACE_MILL_MIDPASS", "POCKETING", "PLANAR_PROFILING", "PLANAR_DEBURRING", "DOCUMENTATION"}
                For Each opName As String In targets
                    For Each o As NXOpen.CAM.Operation In rp.CAMSetup.CAMOperationCollection.ToArray()
                        If o.Name <> opName Then
                            Continue For
                        End If
                        Dim b As OperationBuilder = rp.CAMSetup.CAMOperationCollection.CreateBuilder(o)
                        Try
                            Log("  op " & opName & " builder=" & b.GetType().Name)
                            Dim fs As Object = WalkPath(b, "CutParameters.FloorStock")
                            Log("    FloorStock: " & DumpLeaf(fs))
                            Dim ps As Object = WalkPath(b, "CutParameters.PartStock")
                            Log("    PartStock: " & DumpLeaf(ps))
                            Try
                                WalkPath(b, "CutParameters.FloorStock.Value")
                                SetValue(fs, 0.0)
                                Log("    设 FloorStock.Value=0.0 后同实例读回: " & DumpLeaf(WalkPath(b, "CutParameters.FloorStock")))
                                SetValue(ps, 0.0)
                                Log("    设 PartStock.Value=0.0 后同实例读回: " & DumpLeaf(WalkPath(b, "CutParameters.PartStock")))
                            Catch ex As Exception
                                Log("    设值异常: " & ex.Message)
                            End Try
                        Finally
                            If b IsNot Nothing Then
                                Try
                                    b.Commit()
                                Catch ex As Exception
                                    Log("    Commit 异常: " & ex.Message)
                                End Try
                                Try
                                    b.Destroy()
                                Catch
                                End Try
                            End If
                        End Try
                        ' Commit 后重建 builder 读回（是否持久）
                        Dim b2 As OperationBuilder = Nothing
                        Try
                            b2 = rp.CAMSetup.CAMOperationCollection.CreateBuilder(o)
                            Log("    [Commit 后] FloorStock: " & DumpLeaf(WalkPath(b2, "CutParameters.FloorStock")) & " | PartStock: " & DumpLeaf(WalkPath(b2, "CutParameters.PartStock")))
                        Finally
                            If b2 IsNot Nothing Then
                                Try
                                    b2.Destroy()
                                Catch
                                End Try
                            End If
                        End Try
                        Exit For
                    Next
                Next
            End If

            Log("--- E. 阶段1 三问（两段式提交 / FacingBuilder 属性 / T-005 挂载机制）---")
            Log("  E 前置: m1=" & IIf(m1 Is Nothing, "Nothing", "part") & " rp=" & IIf(rp Is Nothing, "Nothing", "part"))
            Try
                Log("  E rp.CAMSetup 探测: " & IIf(rp.CAMSetup Is Nothing, "(null)", "ok"))
            Catch ex As Exception
                Log("  E rp.CAMSetup 访问异常: " & ex.GetType().Name & " " & ex.Message)
            End Try
            If rp IsNot Nothing AndAlso rp.CAMSetup IsNot Nothing Then
                ' E1. 两段式提交对照（同会话内一段式 vs 两段式，排除会话状态差异）
                Log("  E1a 取 ProgramOrder 根...")
                Dim prog As NCGroup = FindChildOf(rp.CAMSetup, CAMSetup.View.ProgramOrder, "PROGRAM")
                Log("  E1b 取 MachineMethod...")
                Dim meth As NCGroup = FindChildOf(rp.CAMSetup, CAMSetup.View.MachineMethod, "MILL_ROUGH")
                Log("  E1c 取 MachineTool...")
                Dim tool As NCGroup = FindChildOf(rp.CAMSetup, CAMSetup.View.MachineTool, "T-001")
                Log("  E1d 取 Geometry...")
                Dim geom As NCGroup = FindChildOf(rp.CAMSetup, CAMSetup.View.Geometry, "SETUP-005")
                Log("  E1 四父组: " & If(prog Is Nothing, "(null)", prog.Name) & " / " & If(meth Is Nothing, "(null)", meth.Name) & " / " & If(tool Is Nothing, "(null)", tool.Name) & " / " & If(geom Is Nothing, "(null)", geom.Name))
                E1FacingTest(rp.CAMSetup, prog, meth, tool, geom, "E1_FACING_ONESHOT", False)
                E1FacingTest(rp.CAMSetup, prog, meth, tool, geom, "E1_FACING_TWOPHASE", True)

                ' E2. FacingBuilder（FACE_MILLING_MANUAL 的 builder）顶层属性
                For Each o As NXOpen.CAM.Operation In rp.CAMSetup.CAMOperationCollection.ToArray()
                    If o.Name <> "FACE_MILLING_MANUAL" Then
                        Continue For
                    End If
                    Dim b As OperationBuilder = Nothing
                    Try
                        b = rp.CAMSetup.CAMOperationCollection.CreateBuilder(o)
                        Dim topNames As New System.Collections.Generic.List(Of String)
                        For Each pi As PropertyInfo In b.GetType().GetProperties()
                            topNames.Add(pi.Name)
                        Next
                        Log("  E2 FacingBuilder(" & b.GetType().Name & ") 顶层属性: " & String.Join(", ", topNames.ToArray()))
                        Log("  E2 DepthPerCut 路径: " & IIf(WalkPath(b, "DepthPerCut") Is Nothing, "✗ 不存在", "✓ 存在"))
                    Finally
                        If b IsNot Nothing Then
                            Try
                                b.Destroy()
                            Catch
                            End Try
                        End If
                    End Try
                    Exit For
                Next

                ' E3. 本轮 rebuild_part 刀具树 + 各 op 挂载（T-005 机制）
                Dim rootT As NCGroup = rp.CAMSetup.GetRoot(CAMSetup.View.MachineTool)
                Dim names As New System.Collections.Generic.List(Of String)
                For Each m As CAMObject In rootT.GetMembers()
                    Dim g As NCGroup = TryCast(m, NCGroup)
                    If g IsNot Nothing Then
                        names.Add(g.Name)
                    End If
                Next
                Log("  E3 MachineTool 子组: " & String.Join(", ", names.ToArray()))
                For Each o As NXOpen.CAM.Operation In rp.CAMSetup.CAMOperationCollection.ToArray()
                    Dim ptName As String = "(null)"
                    Try
                        ptName = o.ParentMachineTool.Name
                    Catch
                    End Try
                    If o.Name = "PLANAR_DEBURRING" OrElse o.Name = "GROOVE_MILLING" Then
                        Log("  E3 op " & o.Name & " -> ParentMachineTool=" & ptName)
                    End If
                Next
            End If

            Log("--- F. 刀具重定向 + MILL_USER_DEFINED 读取路径（tool 维度最后归因）---")
            If rp IsNot Nothing AndAlso rp.CAMSetup IsNot Nothing Then
                ' F1: rebuild_part 的 PLANAR_PROFILING 挂载 + MachineTool 全子组
                Dim rt As NCGroup = rp.CAMSetup.GetRoot(CAMSetup.View.MachineTool)
                Dim rtNames As New System.Collections.Generic.List(Of String)
                If rt IsNot Nothing Then
                    For Each m As CAMObject In rt.GetMembers()
                        Dim g As NCGroup = TryCast(m, NCGroup)
                        If g IsNot Nothing Then
                            rtNames.Add(g.Name)
                        End If
                    Next
                End If
                Log("  F1 rebuild MachineTool 子组: " & String.Join(", ", rtNames.ToArray()))
                For Each o As NXOpen.CAM.Operation In rp.CAMSetup.CAMOperationCollection.ToArray()
                    If o.Name = "PLANAR_PROFILING" Then
                        Dim ptName As String = "(null)"
                        Try
                            ptName = o.ParentMachineTool.Name
                        Catch
                        End Try
                        Log("  F1 PLANAR_PROFILING -> ParentMachineTool=" & ptName)
                        Exit For
                    End If
                Next
            End If
            If m1 IsNot Nothing AndAlso m1.CAMSetup IsNot Nothing Then
                ' F2: m1 原件的 MILL_USER_DEFINED 组——全 builder 工厂试读
                Dim mud As NCGroup = Nothing
                For Each m As CAMObject In m1.CAMSetup.GetRoot(CAMSetup.View.MachineTool).GetMembers()
                    Dim g As NCGroup = TryCast(m, NCGroup)
                    If g IsNot Nothing AndAlso g.Name = "MILL_USER_DEFINED" Then
                        mud = g
                        Exit For
                    End If
                Next
                If mud Is Nothing Then
                    Log("  F2 未找到 MILL_USER_DEFINED 组")
                Else
                    Log("  F2 MILL_USER_DEFINED 组找到；CAMGroupCollection 单参 Create*Builder 工厂逐个试读：")
                    Dim cc As NCGroupCollection = m1.CAMSetup.CAMGroupCollection
                    For Each mi As MethodInfo In cc.GetType().GetMethods()
                        If mi.Name.IndexOf("Builder", StringComparison.OrdinalIgnoreCase) < 0 Then
                            Continue For
                        End If
                        Dim ps As ParameterInfo() = mi.GetParameters()
                        If ps.Length <> 1 Then
                            Continue For
                        End If
                        Dim b As Object = Nothing
                        Try
                            b = mi.Invoke(cc, New Object() {mud})
                            Dim diaInfo As String = "(无直径属性)"
                            For Each pi As PropertyInfo In b.GetType().GetProperties()
                                If pi.Name.IndexOf("Diam", StringComparison.OrdinalIgnoreCase) >= 0 Then
                                    Try
                                        Dim v As Object = pi.GetValue(b, Nothing)
                                        Dim leaf As Object = WalkPath(v, "Value")
                                        diaInfo = pi.Name & "=" & IIf(leaf Is Nothing, IIf(v Is Nothing, "(null)", v.GetType().Name), leaf.ToString())
                                    Catch ex As Exception
                                        diaInfo = pi.Name & "=(读失败 " & ex.Message & ")"
                                    End Try
                                    Exit For
                                End If
                            Next
                            Log("  F2 ✓ " & mi.Name & " -> " & b.GetType().Name & " | " & diaInfo)
                        Catch ex As Exception
                            Log("  F2 ✗ " & mi.Name & " - " & ex.Message)
                        Finally
                            If b IsNot Nothing Then
                                Try
                                    b.GetType().GetMethod("Destroy").Invoke(b, Nothing)
                                Catch
                                End Try
                            End If
                        End Try
                    Next
                End If
            End If

            Log("--- G. MillFormToolBuilder 读取路径 + 写侧验证 + 重建重定向复验（tool 维度收尾）---")
            ' G1: m1 原件的 MILL_USER_DEFINED 组 → MillFormToolBuilder 全属性
            If m1 IsNot Nothing AndAlso m1.CAMSetup IsNot Nothing Then
                Dim mud As NCGroup = FindChildOf(m1.CAMSetup, CAMSetup.View.MachineTool, "MILL_USER_DEFINED")
                If mud Is Nothing Then
                    Log("  G1 未找到 MILL_USER_DEFINED 组")
                Else
                    Dim b As Object = Nothing
                    Try
                        b = m1.CAMSetup.CAMGroupCollection.CreateMillFormToolBuilder(mud)
                        Log("  G1 CreateMillFormToolBuilder -> " & b.GetType().Name)
                        For Each pi As PropertyInfo In b.GetType().GetProperties()
                            Dim pname As String = pi.Name
                            If pname.IndexOf("Diam", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                               pname.IndexOf("Flute", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                               pname.IndexOf("Corner", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                               pname.IndexOf("Rad", StringComparison.OrdinalIgnoreCase) >= 0 Then
                                Try
                                    Dim v As Object = pi.GetValue(b, Nothing)
                                    Dim leaf As Object = WalkPath(v, "Value")
                                    Log("  G1   " & pname & " : " & IIf(leaf Is Nothing, IIf(v Is Nothing, "(null)", v.GetType().Name), leaf.ToString()))
                                Catch ex As Exception
                                    Log("  G1   " & pname & " : (读失败 " & ex.Message & ")")
                                End Try
                            End If
                        Next
                    Catch ex As Exception
                        Log("  G1 失败: " & ex.Message)
                    Finally
                        If b IsNot Nothing Then
                            Try
                                b.GetType().GetMethod("Destroy").Invoke(b, Nothing)
                            Catch
                            End Try
                        End If
                    End Try
                End If
            End If
            ' G2+G3: rebuild_part 上写参数 + PLANAR_MILL 工序重定向复验
            If rp IsNot Nothing AndAlso rp.CAMSetup IsNot Nothing Then
                Dim mudR As NCGroup = FindChildOf(rp.CAMSetup, CAMSetup.View.MachineTool, "MILL_USER_DEFINED")
                Dim prog As NCGroup = FindChildOf(rp.CAMSetup, CAMSetup.View.ProgramOrder, "PROGRAM")
                Dim meth As NCGroup = FindChildOf(rp.CAMSetup, CAMSetup.View.MachineMethod, "MILL_ROUGH")
                Dim geom As NCGroup = FindChildOf(rp.CAMSetup, CAMSetup.View.Geometry, "SETUP-005")
                If mudR Is Nothing Then
                    Log("  G2 rebuild 无 MILL_USER_DEFINED 组（跳过写侧验证）")
                Else
                    ' G2: 写 HelicalDiameter=90 → Commit → 读回
                    Dim b As Object = Nothing
                    Try
                        b = rp.CAMSetup.CAMGroupCollection.CreateMillFormToolBuilder(mudR)
                        Dim diaProp As PropertyInfo = b.GetType().GetProperty("HelicalDiameter")
                        If diaProp Is Nothing Then
                            Log("  G2 HelicalDiameter 属性不存在（写侧路径需换）")
                        Else
                            Dim leaf As Object = diaProp.GetValue(b, Nothing)
                            Dim valProp As PropertyInfo = If(leaf Is Nothing, Nothing, leaf.GetType().GetProperty("Value"))
                            Dim before As String = IIf(valProp Is Nothing, "(无 Value)", CStr(valProp.GetValue(leaf, Nothing)))
                            If valProp IsNot Nothing Then
                                valProp.SetValue(leaf, 90.0, Nothing)
                            End If
                            b.GetType().GetMethod("Commit").Invoke(b, Nothing)
                            Dim b2 As Object = Nothing
                            Try
                                b2 = rp.CAMSetup.CAMGroupCollection.CreateMillFormToolBuilder(mudR)
                                Dim leaf2 As Object = diaProp.GetValue(b2, Nothing)
                                Dim valProp2 As PropertyInfo = If(leaf2 Is Nothing, Nothing, leaf2.GetType().GetProperty("Value"))
                                Log("  G2 写 HelicalDiameter=90：前 " & before & " → Commit 后 " & IIf(valProp2 Is Nothing, "(无 Value)", CStr(valProp2.GetValue(leaf2, Nothing))))
                            Finally
                                If b2 IsNot Nothing Then
                                    Try
                                        b2.GetType().GetMethod("Destroy").Invoke(b2, Nothing)
                                    Catch
                                    End Try
                                End If
                            End Try
                        End If
                    Catch ex As Exception
                        Log("  G2 失败: " & ex.Message)
                    Finally
                        If b IsNot Nothing Then
                            Try
                                b.GetType().GetMethod("Destroy").Invoke(b, Nothing)
                            Catch
                            End Try
                        End If
                    End Try
                End If
                ' G3: PLANAR_MILL 工序挂（带参数的）MILL_USER_DEFINED → 读 ParentMachineTool
                If prog IsNot Nothing AndAlso meth IsNot Nothing AndAlso mudR IsNot Nothing AndAlso geom IsNot Nothing Then
                    Try
                        Dim op As NXOpen.CAM.Operation = rp.CAMSetup.CAMOperationCollection.Create(
                            prog, meth, mudR, geom, "mill_planar", "PLANAR_MILL",
                            NXOpen.CAM.OperationCollection.UseDefaultName.False, "G3_PLANAR_MUD")
                        Dim ptName As String = "(null)"
                        Try
                            ptName = op.ParentMachineTool.Name
                        Catch
                        End Try
                        Log("  G3 PLANAR_MILL 挂 MILL_USER_DEFINED -> ParentMachineTool=" & ptName & "（MILL_USER_DEFINED=未重定向，其它=重定向）")
                    Catch ex As Exception
                        Log("  G3 失败: " & ex.Message)
                    End Try
                Else
                    Log("  G3 父组缺失: prog=" & If(prog Is Nothing, "null", "ok") & " meth=" & If(meth Is Nothing, "null", "ok") & " mud=" & If(mudR Is Nothing, "null", "ok") & " geom=" & If(geom Is Nothing, "null", "ok"))
                End If
            End If

            Log("=== 结束 ===")
        Catch ex As Exception
            Log("FATAL: " & ex.ToString())
        End Try
        File.WriteAllText(outPath, sb.ToString())
    End Sub

    Private Function OpenOrFind(s As Session, path As String) As Part
        Try
            Dim ls As PartLoadStatus = Nothing
            Dim p As Part = s.Parts.OpenDisplay(path, ls)
            s.Parts.SetWork(p)
            Return p
        Catch
            Try
                For Each p As Part In s.Parts
                    If String.Equals(p.FullPath, path, StringComparison.OrdinalIgnoreCase) Then
                        Return p
                    End If
                Next
            Catch
            End Try
        End Try
        Log("  警告: 零件打开失败 " & path)
        Return Nothing
    End Function

    Private Sub DumpToolTree(p As Part, tag As String)
        If p Is Nothing OrElse p.CAMSetup Is Nothing Then
            Log("  [" & tag & "] 无 CAMSetup")
            Return
        End If
        Dim root As NCGroup = p.CAMSetup.GetRoot(CAMSetup.View.MachineTool)
        Log("  [" & tag & "] MachineTool 根: " & IIf(root Is Nothing, "(null)", root.Name))
        WalkTool(p.CAMSetup, root, 1)
    End Sub

    Private Sub WalkTool(cs As CAMSetup, g As NCGroup, depth As Integer)
        If g Is Nothing Then
            Return
        End If
        Dim t As String = ""
        Try
            t = g.GetNameOfType()
        Catch
        End Try
        Dim readable As String = "?"
        Try
            Dim mb As Object = cs.CAMGroupCollection.CreateMillToolBuilder(g)
            Try
                Dim d As Object = WalkPath(mb, "TlDiameterBuilder.Value")
                readable = "MILL(d=" & IIf(d Is Nothing, "?", d.ToString()) & ")"
            Catch
            Finally
                Try
                    mb.GetType().GetMethod("Destroy").Invoke(mb, Nothing)
                Catch
                End Try
            End Try
            If readable = "?" Then
                Dim db As Object = cs.CAMGroupCollection.CreateDrillStdToolBuilder(g)
                Try
                    readable = "DRILL"
                Catch
                Finally
                    Try
                        db.GetType().GetMethod("Destroy").Invoke(db, Nothing)
                    Catch
                    End Try
                End Try
            End If
        Catch
            readable = "UNREADABLE"
        End Try
        Log(New String(" "c, depth * 2) & g.Name & " | UserName=" & IIf(String.IsNullOrEmpty(g.UserName), "(空)", g.UserName) & " | type=" & t & " | " & readable)
        For Each m As CAMObject In g.GetMembers()
            Dim subg As NCGroup = TryCast(m, NCGroup)
            If subg IsNot Nothing Then
                WalkTool(cs, subg, depth + 1)
            End If
        Next
    End Sub

    ''' <summary>E1：新建 Facing 工序的 stock 写入实验。twoPhase=True → 空 Commit 初始化后
    ''' 再开 builder 写参数二次 Commit（验证「初始化写保护」假设）。</summary>
    Private Sub E1FacingTest(cs As CAMSetup, prog As NCGroup, meth As NCGroup, tool As NCGroup, geom As NCGroup, opName As String, twoPhase As Boolean)
        Try
            Dim op As NXOpen.CAM.Operation = cs.CAMOperationCollection.Create(
                prog, meth, tool, geom, "mill_planar", "FACE_MILL_ZIGZAG",
                NXOpen.CAM.OperationCollection.UseDefaultName.False, opName)
            If twoPhase Then
                ' 第一阶段：空 Commit（完成创建初始化）
                Dim b0 As OperationBuilder = cs.CAMOperationCollection.CreateBuilder(op)
                Try
                    b0.Commit()
                Finally
                    If b0 IsNot Nothing Then
                        Try
                            b0.Destroy()
                        Catch
                        End Try
                    End If
                End Try
            End If
            Dim b As OperationBuilder = cs.CAMOperationCollection.CreateBuilder(op)
            Try
                Dim fs As Object = WalkPath(b, "CutParameters.FloorStock")
                Dim ps As Object = WalkPath(b, "CutParameters.PartStock")
                Log("  E1 " & opName & " twoPhase=" & twoPhase & " 写前: FS=" & DumpLeaf(fs) & " PS=" & DumpLeaf(ps))
                SetValue(fs, 0.0)
                SetValue(ps, 0.0)
                Log("  E1 " & opName & " 写后同实例: FS=" & DumpLeaf(WalkPath(b, "CutParameters.FloorStock")) & " PS=" & DumpLeaf(WalkPath(b, "CutParameters.PartStock")))
                b.Commit()
                Try
                    b.Destroy()
                Catch
                End Try
            Finally
                If b IsNot Nothing Then
                    Try
                        b.Destroy()
                    Catch
                    End Try
                End If
            End Try
            ' Commit 后重建 builder 读回终态
            Dim b2 As OperationBuilder = cs.CAMOperationCollection.CreateBuilder(op)
            Try
                Log("  E1 " & opName & " Commit 后终态: FS=" & DumpLeaf(WalkPath(b2, "CutParameters.FloorStock")) & " PS=" & DumpLeaf(WalkPath(b2, "CutParameters.PartStock")))
            Finally
                If b2 IsNot Nothing Then
                    Try
                        b2.Destroy()
                    Catch
                    End Try
                End If
            End Try
        Catch ex As Exception
            Log("  E1 " & opName & " 失败: " & ex.Message)
        End Try
    End Sub

    Private Function FindChildOf(cs As CAMSetup, view As CAMSetup.View, name As String) As NCGroup
        Try
            Dim root As NCGroup = cs.GetRoot(view)
            If root Is Nothing Then
                Return Nothing
            End If
            For Each m As CAMObject In root.GetMembers()
                Dim g As NCGroup = TryCast(m, NCGroup)
                If g IsNot Nothing AndAlso g.Name = name Then
                    Return g
                End If
            Next
        Catch ex As Exception
            Log("  FindChildOf(" & view.ToString() & ") 异常: " & ex.GetType().Name & " " & ex.Message)
        End Try
        Return Nothing
    End Function

    ''' <summary>叶子对象摘要：Value 值 + 含 Inherit 的属性状态。</summary>
    Private Function DumpLeaf(leaf As Object) As String
        If leaf Is Nothing Then
            Return "(null)"
        End If
        Dim parts As New System.Collections.Generic.List(Of String)
        For Each pi As PropertyInfo In leaf.GetType().GetProperties()
            If pi.Name = "Value" OrElse pi.Name.IndexOf("Inherit", StringComparison.OrdinalIgnoreCase) >= 0 OrElse pi.Name.IndexOf("Status", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Try
                    parts.Add(pi.Name & "=" & pi.GetValue(leaf, Nothing))
                Catch ex As Exception
                    parts.Add(pi.Name & "=(✗ " & ex.Message & ")")
                End Try
            End If
        Next
        Return leaf.GetType().Name & "{" & String.Join(", ", parts.ToArray()) & "}"
    End Function

    Private Sub SetValue(leaf As Object, v As Double)
        If leaf Is Nothing Then
            Return
        End If
        Dim p As PropertyInfo = leaf.GetType().GetProperty("Value")
        If p IsNot Nothing AndAlso p.CanWrite Then
            p.SetValue(leaf, v, Nothing)
        End If
    End Sub

    ''' <summary>点分路径反射走（与 C# ValueExtractor.ReadPath 同思路的 VB 版）。</summary>
    Private Function WalkPath(obj As Object, path As String) As Object
        If obj Is Nothing Then
            Return Nothing
        End If
        Dim cur As Object = obj
        For Each seg As String In path.Split("."c)
            If cur Is Nothing Then
                Return Nothing
            End If
            Try
                cur = cur.GetType().GetProperty(seg).GetValue(cur, Nothing)
            Catch
                Return Nothing
            End Try
        Next
        Return cur
    End Function

    Private Sub Log(ByVal msg As String)
        sb.AppendLine(msg)
        Console.WriteLine(msg)
    End Sub
End Module
