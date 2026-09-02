using Autocam.Plan.Core.Serialization;
using Autocam.PlanExporter.Core.Tests.TestDoubles;
using Xunit;

namespace Autocam.PlanExporter.Core.Tests
{
    public class PlanDeserializerTests
    {
        // 性质：PlanParser 往返一致性——Serialize → Deserialize → Serialize 字节级不变
        //       （与 PlanSerializer 互逆，命名策略同一）。
        // 依据：dev-pattern.md §5 PlanParser 行
        // 失败含义：解析器与序列化器口径不一致，导入侧读到被篡改/丢失字段的 plan。
        [Fact]
        public void Serialize_deserialize_round_trip_is_byte_identical()
        {
            var plan = PlanFixtures.ExportDefault();
            var json = PlanSerializer.Serialize(plan);

            var parsed = PlanDeserializer.Deserialize(json, SchemaAsset.Validator);

            Assert.Equal(json, PlanSerializer.Serialize(parsed));
        }

        // 性质：schema 非法 → 整体拒绝（PlanValidationException），不逐条降级。
        // 依据：plan-executor.md §3.4 前置条件（schema 非法 → 整体拒绝）
        // 失败含义：非法 plan 静默进入重建，产出无意义工程。
        [Fact]
        public void Schema_invalid_json_is_rejected()
        {
            Assert.Throws<PlanValidationException>(
                () => PlanDeserializer.Deserialize("{\"operations\": []}", SchemaAsset.Validator));
        }
    }
}
