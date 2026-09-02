Option Strict Off
Imports System
Imports System.IO
Imports System.Text
Imports System.Reflection
Imports NXOpen
Imports NXOpen.CAM

' M0 组类型探针：dump NCGroup 全成员 + 模板零件四视图组树（从实例反推合法 typeName）。
' 输出 m0_grouptypes.txt。
Module M0GroupTypes
    Private sb As StringBuilder = New StringBuilder()
    Private outPath As String = "C:\nx-vibe-journal-out\m0_grouptypes.txt"

    Sub Main()
        Try
            Dim s As Session = Session.GetSession()
            Log("=== M0 组类型探针 ===")

            Log("--- NCGroup 全成员 ---")
            For Each m As MemberInfo In GetType(NCGroup).GetMembers(BindingFlags.Public Or BindingFlags.Instance Or BindingFlags.Static)
                If m.MemberType = MemberTypes.Property AndAlso Not m.Name.StartsWith("get_") Then
                    Dim pt As PropertyInfo = CType(m, PropertyInfo)
                    Log("  P " & m.Name & " : " & pt.PropertyType.Name)
                End If
            Next

            ' 打开模板零件看真实组
            Dim dst As String = "C:\nx-vibe-journal-out\parts\m0_template.prt"
            Dim tplDir As String = s.GetEnvironmentVariableValue("UGII_CAM_TEMPLATE_PART_ENGLISH_DIR")
            If Not File.Exists(dst) Then File.Copy(tplDir & "mill_planar.prt", dst)
            Dim loadStatus As PartLoadStatus = Nothing
            Dim part1 As Part = s.Parts.OpenDisplay(dst, loadStatus)
            s.Parts.SetWork(part1)
            If Not s.IsCamSessionInitialized Then s.CreateCamSession()
            Dim camSetup As CAMSetup = part1.CAMSetup


            Log("--- NCGroup Type/Name 相关方法 ---")
            For Each mi As MethodInfo In GetType(NCGroup).GetMethods(BindingFlags.Public Or BindingFlags.Instance Or BindingFlags.Static)
                If mi.Name.IndexOf("Type", StringComparison.OrdinalIgnoreCase) >= 0 OrElse mi.Name.IndexOf("Name", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    Log("  M " & mi.ReturnType.Name & " " & mi.Name)
                End If
            Next

            Log("--- typeName 候选试验（模板 CAMSetup 根下建组）---")
            Dim pr As NCGroup = camSetup.GetRoot(CAMSetup.View.ProgramOrder)
            For Each cand As String In New String() {"PROGRAM", "PROGRAM_ORDER", "PROGRAM_GROUP", "MILL_PROGRAM"}
                Try
                    Dim g1 As NCGroup = camSetup.CAMGroupCollection.CreateProgram(pr, cand, "", NXOpen.CAM.OperationCollection.UseDefaultName.False, "PROG_" & cand)
                    Log("CreateProgram typeName 候选 [" & cand & "] ✓")
                Catch ex As Exception
                    Log("CreateProgram typeName 候选 [" & cand & "] ✗ " & ex.Message)
                End Try
            Next
            Dim mr As NCGroup = camSetup.GetRoot(CAMSetup.View.MachineMethod)
            For Each cand As String In New String() {"MILL_METHOD", "METHOD", "MILL_ROUGH"}
                Try
                    Dim g1 As NCGroup = camSetup.CAMGroupCollection.CreateMethod(mr, cand, "", NXOpen.CAM.OperationCollection.UseDefaultName.False, "METH_" & cand)
                    Log("CreateMethod typeName 候选 [" & cand & "] ✓")
                Catch ex As Exception
                    Log("CreateMethod typeName 候选 [" & cand & "] ✗ " & ex.Message)
                End Try
            Next

            Log("--- 模板四视图组树 ---")
            Walk(camSetup.GetRoot(CAMSetup.View.ProgramOrder), 0, "ProgramOrder")
            Walk(camSetup.GetRoot(CAMSetup.View.MachineMethod), 0, "MachineMethod")
            Walk(camSetup.GetRoot(CAMSetup.View.MachineTool), 0, "MachineTool")
            Walk(camSetup.GetRoot(CAMSetup.View.Geometry), 0, "Geometry")

            Log("=== 结束 ===")
        Catch ex As Exception
            Log("FATAL: " & ex.ToString())
        End Try
        File.WriteAllText(outPath, sb.ToString())
    End Sub

    Private Sub Walk(ByVal g As NCGroup, ByVal depth As Integer, ByVal view As String)
        If g Is Nothing Then
            Log("  " & view & " 根: (null)")
            Return
        End If
        Dim indent As String = New String(" "c, depth * 2)
        Dim gt As String = ""
            Try
                gt = g.GetNameOfType()
            Catch
            End Try
            Log(indent & view & ": " & g.Name & "  <type=" & gt & ">")
        For Each m As CAMObject In g.GetMembers()
            Dim subg As NCGroup = TryCast(m, NCGroup)
            If subg IsNot Nothing Then
                Walk(subg, depth + 1, "")
            End If
        Next
    End Sub

    Private Sub Log(ByVal msg As String)
        sb.AppendLine(msg)
        Console.WriteLine(msg)
    End Sub
End Module
