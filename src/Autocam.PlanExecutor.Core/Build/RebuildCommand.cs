using System.Collections.Generic;

namespace Autocam.PlanExecutor.Core.Build
{
    /// <summary>
    /// 重建命令模型（plan-executor.md §2.2）：纯数据，适配层按序执行。
    /// 命令序列 = 刀路输出序的可执行投影，规范顺序见 PlanExecutorPipeline。
    /// </summary>
    public abstract class RebuildCommand
    {
        /// <summary>命令种类标识（测试可读性用，如 CREATE_OPERATION）。</summary>
        public abstract string Kind { get; }
    }

    public sealed class CreateCamSetupCommand : RebuildCommand
    {
        public override string Kind => "CREATE_CAM_SETUP";
    }

    public sealed class CreateMethodGroupCommand : RebuildCommand
    {
        public override string Kind => "CREATE_METHOD_GROUP";
        public string Name { get; set; }
    }

    public sealed class CreateToolGroupCommand : RebuildCommand
    {
        public override string Kind => "CREATE_TOOL_GROUP";
        public string Name { get; set; }
        public Dictionary<string, object> Params { get; set; }
    }

    public sealed class CreateGeometryGroupCommand : RebuildCommand
    {
        public override string Kind => "CREATE_GEOMETRY_GROUP";
        public string Name { get; set; }
        public double[] Origin { get; set; }
        public double[] ZAxis { get; set; }
        public double[] XAxis { get; set; }
        public double? SafePlaneZ { get; set; }
        public int? FixtureOffset { get; set; }
    }

    public sealed class CreateProgramGroupCommand : RebuildCommand
    {
        public override string Kind => "CREATE_PROGRAM_GROUP";
        public string Name { get; set; }

        /// <summary>父组名，null = 根（§2.2 规范顺序：父先于子）。</summary>
        public string ParentName { get; set; }
    }

    /// <summary>工序级参数设置（值来自 plan 拍平值，直通不换算）。</summary>
    public sealed class SetParam
    {
        public string Name { get; set; }
        public object Value { get; set; }
    }

    public sealed class CreateOperationCommand : RebuildCommand
    {
        public override string Kind => "CREATE_OPERATION";
        public string Name { get; set; }
        public string TypeName { get; set; }
        public string SubtypeName { get; set; }
        public string ProgramGroupName { get; set; }
        public string MethodGroupName { get; set; }
        public string ToolGroupName { get; set; }
        public string GeometryGroupName { get; set; }

        /// <summary>
        /// 关联几何兜底锚点（feature.geometry_ref.anchor_point，plan-executor.md 决策点 c：
        /// MVP 用 anchor_point 近似，face_ids 到位后 FaceResolver 升级）。null = 无几何。
        /// 模拟器据此合成面，保证 round-trip 中 feature 锚点可复现。
        /// </summary>
        public double[] AnchorPoint { get; set; }

        /// <summary>ParamRegistry 顺序的有序参数列表（plan-executor.md §3.3：缺字段不产生条目）。</summary>
        public List<SetParam> Params { get; set; } = new List<SetParam>();
    }
}
