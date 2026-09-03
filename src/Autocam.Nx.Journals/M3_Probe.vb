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
