using Autocam.Plan.Core.Policies;
using Xunit;

namespace Autocam.PlanExporter.Core.Tests
{
    public class TypeMappingTests
    {
        // 性质：§4.4 映射表——已知 typeName 命中 operation_type 枚举值与加工域（表驱动，扩表不改码）。
        // 依据：plan-exporter.md §4.4 / nxopen-research.md §4.2 全表
        // 失败含义：映射错位会让重建侧创建错误的工序类型；域错位会让许可检查失效。
        [Theory]
        [InlineData("CAVITY_MILL", "mill_cavity", "MILLING")]
        [InlineData("PLANAR_MILL", "mill_planar", "MILLING")]
        [InlineData("FACE_MILLING", "mill_face", "MILLING")]
        [InlineData("ZLEVEL_PROFILE", "mill_zlevel", "MILLING")]
        [InlineData("CHAMFER_MILL", "mill_chamfer", "MILLING")]
        [InlineData("SPOT_DRILLING", "drill_center", "DRILLING")]
        [InlineData("DRILL", "drill", "DRILLING")]
        [InlineData("PECK_DRILLING", "drill_peck", "DRILLING")]
        [InlineData("TAPPING", "tap", "DRILLING")]
        [InlineData("THREAD_MILLING", "thread_mill", "DRILLING")]
        [InlineData("ROUGH_TURNING", "turn_rough", "TURNING")]
        [InlineData("MULTI_AXIS_DEBURRING", "multi_axis_deburr", "MULTI_AXIS")]
        [InlineData("WEDM_OPERATION", "wedm", "WEDM")]
        [InlineData("ON_MACHINE_PROBING", "probe", "PROBING")]
        [InlineData("MILL_MACHINE_CONTROL", "machine_control", "MACHINE_CONTROL")]
        public void Known_typename_maps(string typeName, string expectedOpType, string expectedDomain)
        {
            Assert.True(TypeMapper.TryMap(typeName, out var opType, out var domain));
            Assert.Equal(expectedOpType, opType);
            Assert.Equal(expectedDomain, domain);
        }

        // 性质：§4.4——未命中不猜测：operation_type="other"、域=UNKNOWN（不参与许可检查），
        //       原始 typeName 由调用侧保留进 nx_template（见 FailureIsolationTests）。
        // 依据：plan-exporter.md §4.4
        // 失败含义：未知类型若被猜测映射，重建侧会创建错误工序；若被丢弃，近似工序失去依据。
        [Fact]
        public void Unknown_typename_maps_to_other()
        {
            Assert.True(TypeMapper.TryMap("SOME_FUTURE_OP", out var opType, out var domain));
            Assert.Equal("other", opType);
            Assert.Equal("UNKNOWN", domain);
        }
    }
}
