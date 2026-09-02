namespace Autocam.Plan.Core.Policies
{
    /// <summary>
    /// 方法组约定命名（plan-executor.md §4.2，决策点 a：plan 不含方法组结构，
    /// 重建时按加工域建默认组；PlanComparer 对比方法组维度时按同一表归一）。
    /// </summary>
    public static class MethodGroupNaming
    {
        public static string ForDomain(string domain)
        {
            switch (domain)
            {
                case TypeMapper.Domains.Milling: return "MILL_ROUGH";
                case TypeMapper.Domains.Drilling: return "DRILL_METHOD";
                case TypeMapper.Domains.Turning: return "TURN_METHOD";
                case TypeMapper.Domains.MultiAxis: return "MULTI_AXIS_METHOD";
                case TypeMapper.Domains.Wedm: return "WEDM_METHOD";
                case TypeMapper.Domains.Additive: return "ADDITIVE_METHOD";
                case TypeMapper.Domains.Probing: return "PROBE_METHOD";
                case TypeMapper.Domains.MachineControl: return "MACHINE_METHOD";
                case TypeMapper.Domains.UserDefined: return "USER_METHOD";
                default: return "METHOD";   // UNKNOWN（other + nx_template 直落）
            }
        }
    }
}
