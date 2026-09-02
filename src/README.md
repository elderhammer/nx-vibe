# Autocam.PlanExporter

PlanExporter 三步闭环第①步（ground truth 采集）的实现。设计依据：[plan-exporter.md](../plan-exporter.md)、
[nx-plugin-design.md](../nx-plugin-design.md)、[nxopen-research.md](../nxopen-research.md)。
合同：[schema/autocam-plan.schema.json](../schema/autocam-plan.schema.json)。

## 结构

| 项目 | 说明 |
|---|---|
| `Autocam.PlanExporter.Core` | 纯 C#（net48，零 NXOpen 引用）：快照 DTO + 导出管线 + 序列化/schema 校验 |
| `Autocam.PlanExporter.Core.Tests` | xUnit 单测：12 套件 54 用例，全部不依赖 NX |
| （未建）NX 适配层 | 薄壳：NX API → `CamSetupSnapshot`。需 NX SDK，后续在 NX 侧集成验证 |

## 隔离设计（对应 plan-exporter.md 的性质）

- **快照边界 = I1（只读）的结构性保证**：Core 只消费 `CamSetupSnapshot` 纯数据，
  编译期杜绝写会话。会话安全性（零写调用、Builder 用毕即 Destroy）的**测试**随适配层
  在 NX 侧做集成验证（nx-plugin-design.md §4），不属单测范围。
- **确定性**（§3.1e）：输出顺序全部来自有序列表/树前序；组装期不做任何 Dictionary 迭代；
  ID 每次导出独立分配；plan_id 由 PartName 派生（进程级计数会破坏幂等）。
  由 DeterminismTests 的字节级断言锁定。
- **单调性**（§3.1b/c/d）：生效值拍平固化继承来源 → 条目相互独立；workplan 前序投影保序。
  由 ResolvedValueTests / OrderMonotonicityTests 锁定——这些是 PlanComparer
  "逐工序独立对比 + 序列对齐"（nx-plugin-design.md §2.2）的上游保证。
- **失败隔离**（I7）：许可缺失/父组缺失/能力探测失败只影响单工序或单参数，绝不拖垮整份导出。
- **策略数据与遍历逻辑隔离**：类型映射表（`TypeMapper`，nxopen-research §4.2 全表）、
  参数表（`ParamRegistry`，MVP 清单）均为数据表，扩表不改码。

## 关键决策（已拍板）

- schema 最小必填（plan_id/operations/workingsteps/workplan），其余可选；
  导出完整性由后置条件 + diagnostics 把关（缺项显式落诊断，绝不静默省略）
- 枚举大写风格；operation_type 保持小写蛇形；封闭枚举读到未知 NX 值 → 省字段 + warning
- ID 为不透明字符串（格式不作合同），只保证全局唯一 + 确定
- workplan 节点形状 `{name, workingstep_ref, children}` 为推断（文档未钉死，
  PlanExecutor 设计时如需调整，需同步改 schema + 测试）
- 类名 `PlanExportPipeline`（类名 `PlanExporter` 与命名空间 `Autocam.PlanExporter`
  存在 C# 简单名冲突，任何嵌套命名空间下的消费方都会命中）

## 测试套件 ↔ 性质映射

见各测试文件头部三行注释（性质 / 依据 / 失败含义）。汇总：
SchemaContract（§3.3-1/2）、Determinism（§3.1e）、ResolvedValue（§4.3/§3.1b/d）、
OrderMonotonicity（§3.1c/I4）、ReferenceClosure（§3.3-4）、Bijection（§3.3-5/I3）、
FailureIsolation（I7/§3.2）、TypeMapping（§4.4）、Anchor（§4.5/I5）、
Precondition（§3.2 致命项）、DiagnosticsContract（§4.7）、UnitConvention（§3.3-8/I6）。

## 运行

```bash
dotnet test Autocam.PlanExporter.sln   # 54 用例，net48，无 NX 依赖
```

schema 文件作为测试资产拷入输出目录——schema 变更直接触发契约测试红，双向锁定。

## 遗留（下一步）

1. NX 适配层：CAMSetup/Builder/几何 API → `CamSetupSnapshot`（含生效值探测、能力探测、许可探测）
2. 适配层集成验证：手编工程 → 导出 → 同一工程内重建 → 对比（nx-plugin-design.md §4）
3. PlanParser（强类型模型，可直接由 schema 生成）、PlanExecutor、PlanComparer 未开始
4. schema 增强字段（非切削细分/避让点/多轴驱动）按"可选增强"后补
