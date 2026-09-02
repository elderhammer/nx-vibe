using System.Collections.Generic;
using Autocam.Plan.Core.Plan;
using Autocam.PlanExporter.Core.Tests.TestDoubles;

namespace Autocam.PlanExecutor.Core.Tests.TestDoubles
{
    /// <summary>
    /// 执行器测试夹具：手写 plan（不经过导出器），覆盖稀疏/嵌套/域顺序等场景。
    /// 完整夹具复用 PlanFixtures.ExportDefault()（同一批夹具既喂导出又喂重建，
    /// 是 round-trip 测试的前提）。
    /// </summary>
    public static class ExecutorFixtures
    {
        /// <summary>完整 plan（由导出器夹具产出）。</summary>
        public static PlanRoot FullPlan() => PlanFixtures.ExportDefault();

        /// <summary>稀疏 plan：仅 cycle/直径/MCS 有值，无 feature 几何——继承语义测试输入。</summary>
        public static PlanRoot SparseDrillPlan()
        {
            var op = new OperationEntry
            {
                OperationId = "OP-X",
                OperationType = "drill",
                ToolRef = "T-X",
                NxTemplate = new NxTemplateEntry { Type = "DRILL", Subtype = "" },
            };
            op.Strategy["cycle"] = "PECK";
            var tool = new ToolEntry { ToolId = "T-X", Diameter = 6.8 };
            var setup = new SetupEntry
            {
                SetupId = "SET-X",
                Mcs = new McsEntry
                {
                    Origin = new[] { 0.0, 0.0, 0.0 },
                    ZAxis = new[] { 0.0, 0.0, 1.0 },
                    XAxis = new[] { 1.0, 0.0, 0.0 },
                },
            };
            var feature = new FeatureEntry { FeatureId = "F-X", FeatureType = "hole" };
            var ws = new WorkingstepEntry { WorkingstepId = "WS-X", OperationRef = "OP-X", FeatureRef = "F-X", SetupRef = "SET-X" };
            var leaf = new WorkplanNodeEntry { Name = "DRILL_X", WorkingstepRef = "WS-X" };
            var program1 = new WorkplanNodeEntry { Name = "PROGRAM_1", Children = { leaf } };
            var root = new WorkplanNodeEntry { Name = "PROGRAM", Children = { program1 } };
            return new PlanRoot
            {
                PlanId = "PLAN-X",
                Name = "x.prt",
                Operations = { op },
                Workingsteps = { ws },
                Features = { feature },
                Setups = { setup },
                Resources = { Tools = { tool } },
                Workplan = new WorkplanEntry { Root = root, Elements = { root, program1, leaf } },
            };
        }

        /// <summary>两工序 plan：钻孔在前、铣在后（方法组域首次出现序测试输入）。</summary>
        public static PlanRoot TwoOpPlan()
        {
            var drill = new OperationEntry
            {
                OperationId = "OP-D",
                OperationType = "drill",
                ToolRef = "T-D",
                NxTemplate = new NxTemplateEntry { Type = "DRILL", Subtype = "" },
            };
            drill.Strategy["cycle"] = "PECK";
            var mill = new OperationEntry
            {
                OperationId = "OP-M",
                OperationType = "mill_cavity",
                ToolRef = "T-M",
                NxTemplate = new NxTemplateEntry { Type = "CAVITY_MILL", Subtype = "" },
            };
            mill.Strategy["depth_per_cut"] = 2.0;

            var tools = new[] { new ToolEntry { ToolId = "T-D", Diameter = 6.8 }, new ToolEntry { ToolId = "T-M", Diameter = 10.0 } };
            var setup = new SetupEntry
            {
                SetupId = "SET-X",
                Mcs = new McsEntry { Origin = new[] { 0.0, 0.0, 0.0 }, ZAxis = new[] { 0.0, 0.0, 1.0 }, XAxis = new[] { 1.0, 0.0, 0.0 } },
            };
            var features = new[] { new FeatureEntry { FeatureId = "F-D", FeatureType = "hole" }, new FeatureEntry { FeatureId = "F-M", FeatureType = "pocket" } };
            var wsList = new[]
            {
                new WorkingstepEntry { WorkingstepId = "WS-D", OperationRef = "OP-D", FeatureRef = "F-D", SetupRef = "SET-X" },
                new WorkingstepEntry { WorkingstepId = "WS-M", OperationRef = "OP-M", FeatureRef = "F-M", SetupRef = "SET-X" },
            };
            var leafD = new WorkplanNodeEntry { Name = "DRILL_X", WorkingstepRef = "WS-D" };
            var leafM = new WorkplanNodeEntry { Name = "MILL_X", WorkingstepRef = "WS-M" };
            var program1 = new WorkplanNodeEntry { Name = "PROGRAM_1", Children = { leafD, leafM } };
            var root = new WorkplanNodeEntry { Name = "PROGRAM", Children = { program1 } };
            var plan = new PlanRoot
            {
                PlanId = "PLAN-T",
                Name = "two.prt",
                Workplan = new WorkplanEntry { Root = root, Elements = { root, program1, leafD, leafM } },
            };
            plan.Operations.AddRange(new[] { drill, mill });
            plan.Workingsteps.AddRange(wsList);
            plan.Features.AddRange(features);
            plan.Setups.Add(setup);
            plan.Resources.Tools.AddRange(tools);
            return plan;
        }

        /// <summary>嵌套 workplan：PROGRAM → SUB → PROGRAM_1 → 叶子（父先于子测试输入）。</summary>
        public static PlanRoot NestedWorkplanPlan()
        {
            var plan = SparseDrillPlan();
            var leaf = new WorkplanNodeEntry { Name = "DRILL_X", WorkingstepRef = "WS-X" };
            var program1 = new WorkplanNodeEntry { Name = "PROGRAM_1", Children = { leaf } };
            var sub = new WorkplanNodeEntry { Name = "SUB", Children = { program1 } };
            var root = new WorkplanNodeEntry { Name = "PROGRAM", Children = { sub } };
            plan.Workplan = new WorkplanEntry { Root = root, Elements = { root, sub, program1, leaf } };
            return plan;
        }
    }
}
