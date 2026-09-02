# Autocam.PlanExporter / PlanExecutor / PlanComparer

三步闭环（ground truth 采集 / 自动重建 / 对比验证）的实现。设计依据：
[plan-exporter.md](../plan-exporter.md)、[plan-executor.md](../plan-executor.md)、
[plan-comparer.md](../plan-comparer.md)、[nx-plugin-design.md](../nx-plugin-design.md)、
[nxopen-research.md](../nxopen-research.md)。
开发模式：[dev-pattern.md](../dev-pattern.md)。
合同：[schema/autocam-plan.schema.json](../schema/autocam-plan.schema.json) +
[schema/autocam-compare-report.schema.json](../schema/autocam-compare-report.schema.json)。

## 结构

| 项目 | 说明 |
|---|---|
| `Autocam.Plan.Core` | 共享合同层：plan 模型 / 快照 DTO / 能力画像 / 诊断 / 策略表（TypeMapper/ParamRegistry/MethodGroupNaming）/ 序列化 + schema 校验（rule of three 抽取，决策点 D1） |
| `Autocam.PlanExporter.Core` | 纯 C#（net48，零 NXOpen）：导出管线（快照 → plan 对象图） |
| `Autocam.PlanExecutor.Core` | 纯 C#（net48，零 NXOpen）：plan → 有序参数化命令序列 + 模拟快照 S′ |
| `Autocam.PlanComparer.Core` | 纯 C#（net48，零 NXOpen）：plan×plan → 偏差报告（对齐 + 容差引擎 + 七维度 + 评分） |
| `Autocam.PlanExporter.Core.Tests` | xUnit：12 套件 56 用例，零 NX 依赖 |
| `Autocam.PlanExecutor.Core.Tests` | xUnit：8 套件 31 用例，零 NX 依赖（复用导出器夹具做 round-trip） |
| `Autocam.PlanComparer.Core.Tests` | xUnit：13 套件 48 用例，零 NX 依赖（复用导出器夹具做零基线验证） |
| `Autocam.Nx.Adapter` | NX 适配层薄壳（`Autocam.Plugins.sln`，依赖本机 NXOpen SDK）：会话引导 / 导出快照读取器 / 命令执行器 / 参数路径表 + Journal 入口。M1/M3 批处理实测达标；M2 执行验证在 GUI 会话 |
| `Autocam.Nx.Journals` | VB 装载器 journal：M0 探针系列（API 预研记录）+ M1_Export / M2_Rebuild / M3_Partial 验证入口（运行方式见 [nx-journal-manual-verification.md](../nx-journal-manual-verification.md)） |

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
- **对比的对齐保真**（plan-comparer.md §3.3）：类型多重集相等 → 实例序配对（纯置换
  只报 order_swap）；不等 → 贪心配对（匹配数 = LCS 最大长度）+ 同位置 type_mismatch。
  配对表是七维度字段比较的唯一入口，未配对工序绝不产生参数/刀具/几何偏差行。
- **对比的对称口径**（§3.2）：容差判定用 |Δ|、评分分母取 max、missing ↔ extra 互换——
  左右互换报告结论不漂移；评分公式只吃容差口径数值字段（§2.3），可逐行复算。

## 关键决策（已拍板，详见两份性质文档）

- schema 最小必填（plan_id/operations/workingsteps/workplan），其余可选；
  枚举大写风格；operation_type 保持小写蛇形；封闭枚举给 "other" 兜底
- ID 为不透明字符串（格式不作合同，测试从 plan 读取而非硬编码）
- 方法组重建约定：plan 不含方法组结构，按加工域建默认组（plan-executor.md §4.2 命名表）；
  PlanComparer 对比方法组维度时按同一表归一
- workplan 节点形状 `{name, workingstep_ref, children}` 已由执行器作为第一个消费方确认
- 几何兜底：MVP 用 anchor_point（模拟器合成面），face_ids 到位后 FaceResolver 升级
- 类名 `PlanExportPipeline`/`PlanExecutorPipeline`/`PlanComparePipeline`（类名与命名空间段同名存在 C# 简单名冲突）
- schema 校验库用 NJsonSchema（MIT；Newtonsoft.Json.Schema 为商业许可）
- 对比决策 D2-D7（plan-comparer.md §7 决策记录）：位置归一对齐 / 报告立 schema /
  容差表（0.01mm 绝对、5% 相对、枚举精确、向量欧氏）/ 刀路维度预留 / 已知跳过按
  能力画像结构化判定 / 逐条偏差行 + 汇总评分双粒度

## 测试套件 ↔ 性质映射

见各测试文件头部三行注释（性质/依据/失败含义）。导出侧：
SchemaContract、Determinism、ResolvedValue、OrderMonotonicity、ReferenceClosure、
Bijection、FailureIsolation、TypeMapping、Anchor、Precondition、DiagnosticsContract、
UnitConvention、PlanDeserializer。执行侧：
RoundTrip、CommandOrder、GroupTreeRebuild、InheritanceSemantics、Precondition、
Mapping、Capability、DiagnosticsContract。对比侧：
Reflexivity、Symmetry、Alignment、ToleranceBoundary、ToolComparison、ParamComparison、
McsGeometry、Scoring、ReportContract、KnownSkip、Determinism、ZeroBaseline、Precondition。

## 运行

```bash
dotnet test Autocam.PlanExporter.sln   # 135 用例，net48，零 NX 依赖
```

schema 文件作为测试资产拷入输出目录——schema 变更直接触发契约测试红，双向锁定。

## 遗留（下一步）

1. NX 适配层收尾（GUI 会话，见 [nx-journal-manual-verification.md](../nx-journal-manual-verification.md)）：
   执行侧 M2 核对清单（组/工序 Create 在批处理下受对象模板注册表限制）；导出侧增强
   （工序关联几何读取、能力探测、按 builder 类型参数子集表降噪）
2. NX 内完整闭环 GUI 验证：导出 → 重建 → 再导出 → 跨件对比（nx-plugin-design.md §4）
3. 对比增强：刀路维度（schema 已预留 toolpath 通道，Operation.GetToolpathTime/Length 已定位）、
   几何维度 FaceResolver 面集匹配升级（MVP 由 anchor_point 兜底）
4. schema 增强字段（非切削细分/避让点/多轴驱动）按"可选增强"后补
