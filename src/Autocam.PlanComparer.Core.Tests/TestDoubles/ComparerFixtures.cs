using System.Collections.Generic;
using System.Linq;
using Autocam.Plan.Core.Dto;
using Autocam.Plan.Core.Plan;
using Autocam.PlanComparer.Core.Compare;
using Autocam.PlanComparer.Core.Report;

namespace Autocam.PlanComparer.Core.Tests.TestDoubles
{
    /// <summary>
    /// 对比测试夹具：手写 plan 对（每调用全新实例，可安全局部变异）。
    /// 单工序 drill（全字段）+ 双工序 drill/mill（顺序/置换测试），字段值选用
    /// 双精度可精确表示的数值（容差边界测试专用值另在测试内构造，见 ToleranceBoundaryTests）。
    /// </summary>
    public static class ComparerFixtures
    {
        // ---- 上下文 ----

        public static CompareContext Context() => new CompareContext();

        public static CompareContext WithUnsupported(params string[] fields)
        {
            var cap = new CapabilityProfile();
            foreach (var f in fields)
            {
                cap.UnsupportedParams.Add(f);
            }
            return new CompareContext { RightCapability = cap };
        }

        public static CompareContext WithUnwritable(string planType, params string[] fields)
        {
            var cap = new CapabilityProfile();
            cap.UnwritableByPlanType[planType] = new System.Collections.Generic.HashSet<string>(fields);
            return new CompareContext { RightCapability = cap };
        }

        // ---- plan 构造 ----

        /// <summary>单工序 drill：tool/setup/mcs/anchor/strategy/technology 全字段。</summary>
        public static PlanRoot OneOpPlan()
        {
            var op = new OperationEntry
            {
                OperationId = "OP-1",
                OperationType = "drill",
                ToolRef = "T-1",
                NxTemplate = new NxTemplateEntry { Type = "DRILL", Subtype = "" },
            };
            op.Strategy["cycle"] = "PECK";
            op.Strategy["depth"] = new Dictionary<string, object> { { "mode", "THROUGH" }, { "value", 25.0 } };
            op.Technology["spindle_rpm"] = 1200;
            op.Technology["feed_cut"] = new Dictionary<string, object> { { "value", 120.0 }, { "unit", "MMPM" } };

            var tool = new ToolEntry { ToolId = "T-1", Type = "DRILL", Diameter = 6.8, NumFlutes = 2 };
            var setup = new SetupEntry
            {
                SetupId = "SET-1",
                Mcs = new McsEntry
                {
                    Origin = new[] { 0.0, 0.0, 0.0 },
                    ZAxis = new[] { 0.0, 0.0, 1.0 },
                    XAxis = new[] { 1.0, 0.0, 0.0 },
                },
                SafePlaneZ = 50.0,
                FixtureOffset = 1,
            };
            var feature = new FeatureEntry
            {
                FeatureId = "F-1",
                FeatureType = "hole",
                GeometryRef = new GeometryRefEntry { AnchorPoint = new[] { 10.0, 20.0, 5.0 } },
            };
            var ws = new WorkingstepEntry { WorkingstepId = "WS-1", OperationRef = "OP-1", FeatureRef = "F-1", SetupRef = "SET-1" };
            var leaf = new WorkplanNodeEntry { Name = "DRILL_1", WorkingstepRef = "WS-1" };
            var program1 = new WorkplanNodeEntry { Name = "PROGRAM_1", Children = { leaf } };
            var root = new WorkplanNodeEntry { Name = "PROGRAM", Children = { program1 } };

            var plan = new PlanRoot
            {
                PlanId = "PLAN-1",
                Name = "one.prt",
                InputRef = "one.step",
                Workplan = new WorkplanEntry { Root = root, Elements = { root, program1, leaf } },
            };
            plan.Operations.Add(op);
            plan.Workingsteps.Add(ws);
            plan.Features.Add(feature);
            plan.Setups.Add(setup);
            plan.Resources.Tools.Add(tool);
            return plan;
        }

        /// <summary>双工序 plan：DRILL_X(drill) 在前、MILL_X(mill_cavity) 在后（同一 setup）。</summary>
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
            drill.Technology["spindle_rpm"] = 1200;

            var mill = new OperationEntry
            {
                OperationId = "OP-M",
                OperationType = "mill_cavity",
                ToolRef = "T-M",
                NxTemplate = new NxTemplateEntry { Type = "CAVITY_MILL", Subtype = "" },
            };
            mill.Strategy["depth_per_cut"] = 2.0;
            mill.Technology["spindle_rpm"] = 6000;

            var tools = new[]
            {
                new ToolEntry { ToolId = "T-D", Type = "DRILL", Diameter = 6.8, NumFlutes = 2 },
                new ToolEntry { ToolId = "T-M", Type = "END_MILL", Diameter = 10.0, NumFlutes = 4 },
            };
            var setup = new SetupEntry
            {
                SetupId = "SET-X",
                Mcs = new McsEntry
                {
                    Origin = new[] { 0.0, 0.0, 0.0 },
                    ZAxis = new[] { 0.0, 0.0, 1.0 },
                    XAxis = new[] { 1.0, 0.0, 0.0 },
                },
                SafePlaneZ = 50.0,
            };
            var features = new[]
            {
                new FeatureEntry { FeatureId = "F-D", FeatureType = "hole", GeometryRef = new GeometryRefEntry { AnchorPoint = new[] { 10.0, 20.0, 5.0 } } },
                new FeatureEntry { FeatureId = "F-M", FeatureType = "pocket", GeometryRef = new GeometryRefEntry { AnchorPoint = new[] { 30.0, 20.0, 0.0 } } },
            };
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
                PlanId = "PLAN-2",
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

        /// <summary>workplan 前序叶子名序列（对齐测试断言用）。</summary>
        public static List<string> LeafNames(PlanRoot plan)
        {
            return plan.Workplan.Elements.Where(n => !string.IsNullOrEmpty(n.WorkingstepRef)).Select(n => n.Name).ToList();
        }

        // ---- 断言辅助 ----

        public static DeviationEntry[] Rows(ComparisonReport report, string dimension)
        {
            return report.Deviations.Where(r => r.Dimension == dimension).ToArray();
        }

        public static DeviationEntry[] Rows(ComparisonReport report, string dimension, string kind)
        {
            return report.Deviations.Where(r => r.Dimension == dimension && r.Kind == kind).ToArray();
        }
    }
}
