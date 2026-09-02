# Autocam.PlanExporter / PlanExecutor

三步闭环第①②步（ground truth 采集 / 自动重建）的实现。设计依据：
[plan-exporter.md](../plan-exporter.md)、[plan-executor.md](../plan-executor.md)、
[nx-plugin-design.md](../nx-plugin-design.md)、[nxopen-research.md](../nxopen-research.md)。
开发模式：[dev-pattern.md](../dev-pattern.md)。
合同：[schema/autocam-plan.schema.json](../schema/autocam-plan.schema.json)。

## 结构

| 项目 | 说明 |
|---|---|
| `Autocam.PlanExporter.Core` | 纯 C#（net48，零 NXOpen）：快照 DTO + 导出管线 + 序列化/schema 校验（含 PlanDeserializer，即 PlanParser 轻量版） |
| `Autocam.PlanExecutor.Core` | 纯 C#（net48，零 NXOpen）：plan → 有序参数化命令序列 + 模拟快照 S′ |
| `Autocam.PlanExporter.Core.Tests` | xUnit：12 套件 56 用例，零 NX 依赖 |
| `Autocam.PlanExecutor.Core.Tests` | xUnit：8 套件 31 用例，零 NX 依赖（复用导出器夹具做 round-trip） |
| （未建）NX 适配层 ×2 | 薄壳：NX API → `CamSetupSnapshot`（导出侧）；命令序列 → NX Builder 调用（执行侧）。需 NX SDK，后续在 NX 侧集成验证 |

> 共享合同层（PlanModel/CapabilityProfile/DiagnosticsCollector/TypeMapper/ParamRegistry/
> Serialization）目前落在 PlanExporter.Core；第三个消费者（PlanComparer）出现时按
> rule of three 抽取公共项目。

## 隔离设计（对应两份性质文档）

- **快照/命令边界 = 会话安全的保证**：导出侧 Core 只消费 `CamSetupSnapshot` 纯数据，
  执行侧 Core 只产出命令对象——编译期杜绝写会话/碰 NX。NX 交互的测试随适配层在
  NX 侧集成验证（nx-plugin-design.md §4）。
- **确定性**（双方 §3.1e/§3.2）：输出顺序全部来自有序列表/树前序；不做字典迭代输出；
  ID 每次调用独立分配；命令序列字节级可复现。
- **Round-trip 圆环（执行器核心性质）**：`Export(Rebuild(plan)) ≡ plan`（完整 plan 逐字段
  等价、稀疏 plan 字段集等价）——「plan 合同能否无歧义重建」在 Core 内零部署验证，
  由 RoundTripTests 锁定。执行器双产物（命令序列 + 模拟快照 S′）是该性质的前提。
- **继承语义（不伪造值）**：导出侧缺字段 → 诊断必增；执行侧缺字段 → 不产生 Set 命令，
  由 NX 继承组/模板默认（nx-plugin-design.md §5 注释）。
- **失败隔离**（I7 镜像）：引用悬空/类型不可映射/能力不支持只影响单条目或单参数。
- **策略数据与遍历逻辑隔离**：TypeMapper 正/反向表、ParamRegistry、方法组约定命名表
  均为数据表，扩表不改码。

## 关键决策（已拍板，详见两份性质文档）

- schema 最小必填（plan_id/operations/workingsteps/workplan），其余可选；
  枚举大写风格；operation_type 保持小写蛇形；封闭枚举给 "other" 兜底
- ID 为不透明字符串（格式不作合同，测试从 plan 读取而非硬编码）
- 方法组重建约定：plan 不含方法组结构，按加工域建默认组（plan-executor.md §4.2 命名表）；
  PlanComparer 对比方法组维度时按同一表归一
- workplan 节点形状 `{name, workingstep_ref, children}` 已由执行器作为第一个消费方确认
- 几何兜底：MVP 用 anchor_point（模拟器合成面），face_ids 到位后 FaceResolver 升级
- 类名 `PlanExportPipeline`/`PlanExecutorPipeline`（类名与命名空间段同名存在 C# 简单名冲突）
- schema 校验库用 NJsonSchema（MIT；Newtonsoft.Json.Schema 为商业许可）

## 测试套件 ↔ 性质映射

见各测试文件头部三行注释（性质/依据/失败含义）。导出侧：
SchemaContract、Determinism、ResolvedValue、OrderMonotonicity、ReferenceClosure、
Bijection、FailureIsolation、TypeMapping、Anchor、Precondition、DiagnosticsContract、
UnitConvention、PlanDeserializer。执行侧：
RoundTrip、CommandOrder、GroupTreeRebuild、InheritanceSemantics、Precondition、
Mapping、Capability、DiagnosticsContract。

## 运行

```bash
dotnet test Autocam.PlanExporter.sln   # 87 用例，net48，零 NX 依赖
```

schema 文件作为测试资产拷入输出目录——schema 变更直接触发契约测试红，双向锁定。

## 遗留（下一步）

1. NX 适配层 ×2：导出侧（CAMSetup/Builder/几何 API → 快照，含生效值/能力/许可探测）、
   执行侧（命令序列 → NXOpen 调用 + STEP 打开）
2. NX 内最小闭环集成验证：手编工程 → 导出 → 同一工程内重建 → 对比（nx-plugin-design.md §4）
3. PlanComparer（纯函数 plan×plan，依赖已固化的单调性/确定性性质）
4. FaceResolver（OCCT face_id → NX Tag 匹配；MVP 由 anchor_point 兜底）
5. schema 增强字段（非切削细分/避让点/多轴驱动）按"可选增强"后补
