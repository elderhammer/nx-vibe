# PlanExecutor 分析 — 数据结构 / 性质 / 算法

> 更新时间：2026-09-02
> 分析对象：nx-plugin-design.md §2 定义的 `PlanExecutor` 模块（三步闭环第②步）
> 前置阅读：[nx-plugin-design.md](./nx-plugin-design.md)、[plan-exporter.md](./plan-exporter.md)、
> [dev-pattern.md](./dev-pattern.md)（本模块按此模式开发）
>
> 定位：PlanExporter 的镜像。**Core 部分（本文对象）只做 plan → 有序参数化命令序列**，
> 不碰 NX（命令执行属适配层，NX 侧集成验证）；同时产出**模拟快照 S′**，使
> 「Export(Rebuild(plan)) ≡ plan」的 round-trip 性质可以在 Core 内零部署验证——
> 这是闭环要回答的「plan 合同能否无歧义重建」的预演。

---

## 1. 职责与边界

```
plan.json（PlanRoot）
  └─ PlanExecutorPipeline（Core，纯 C#）
       ├─ 前置校验 → 引用闭合 → 生存判定 → 命令生成 → 模拟 S′
       ├─ 输出①：RebuildPlan 命令序列（适配层按序执行 → NX 建工程 → prj′）
       └─ 输出②：S′（CamSetupSnapshot，重建结果的纯数据投影，供 round-trip 测试）
```

- **只读 plan，不读 NX**：与导出器镜像，快照/命令边界保证 Core 零 NXOpen。
- **不执行命令**：命令的 NXOpen 落点（Create/Builders）属适配层，见 nxopen-research.md §3.2。
- 关键口径（nx-plugin-design.md §5 注释）：**导入时缺省字段允许继承组默认值**——
  缺字段 → 不产生 Set 命令 → NX 侧继承，绝不伪造值。

## 2. 数据结构

### 2.1 输入：PlanRoot（与导出器共享，见 schema v3）

```
workplan.root（Program 组树前序投影）  → 组树与工序序的唯一权威来源
setups[]（MCS/安全平面/夹具偏置）      → Geometry 组
resources.tools[]                      → Tool 组
operations[]（拍平生效值 + nx_template）→ 工序 + Set 参数
features[]（geometry_ref.anchor_point）→ 关联几何兜底（face_ids 云端回填）
diagnostics[]（输入侧诊断，不参与重建）
```

> 注意：**plan 不含方法组结构**（MVP 清单无 method_ref）。重建时按约定建默认组（§4.2），
> 方法组维度与 ground truth 的差异由 PlanComparer 按约定归一（决策点 a，已拍板选 A）。

### 2.2 输出①：RebuildPlan 命令序列

| 命令 | 关键字段 |
|:---|:---|
| `CreateCamSetupCommand` | — |
| `CreateMethodGroupCommand` | name（约定名，§4.2） |
| `CreateToolGroupCommand` | name（=tool_id），params（MVP 刀具字段） |
| `CreateGeometryGroupCommand` | name（=setup_id），origin/z_axis/x_axis/safe_plane_z/fixture_offset |
| `CreateProgramGroupCommand` | name，parentName（嵌套路径，null=根） |
| `CreateOperationCommand` | name，typeName，subtypeName，四父组名，anchor_point（几何兜底，可空），params（SetParam 有序列表） |

**规范顺序**（命令序列 = 刀路输出序的可执行投影）：CamSetup → 方法组 → 刀具组 →
几何组 → Program 组（前序、父先于子）→ 工序（workplan 叶子序）。

### 2.3 输出②：模拟快照 S′

命令序列在内存状态上的执行结果：四视图组树 + 工序（显式参数 = plan 出现的字段，
继承态 = plan 缺省字段）+ 合成面（anchor_point → Centroid）。S′ 直接喂给
PlanExporter 完成 round-trip 验证。

## 3. 性质

### 3.1 Round-trip（核心性质）

设 plan 为合法输入，`S′ = Simulate(Build(plan))`，`plan′ = Export(S′)`，则：

**(a) 完整 plan 等价**：plan′ 与 plan 逐字段相等（ID 按位置归一：操作/工步/特征/刀具/
setup 序号一一对应）。涵盖 operation_type/nx_template/strategy/technology 全字段、
刀具全字段、MCS/安全平面/夹具偏置、workplan 树形与前序、anchor_point。

**(b) 稀疏 plan 等价**：plan 缺字段时，plan′ 的**字段集与 plan 相同**（缺的仍缺、
present 的逐字段相等）——继承语义下"不伪造值"的必然推论。

**(c) 自洽前提**：该性质只承诺「合同无歧义地记录了意图」，不承诺「意图本身与 ground
truth 一致」——后者是闭环第③步 PlanComparer 的职责（与导出器 §3.3 元后置条件同构）。

### 3.2 保序与组树还原

- 命令序 = workplan 前序（§2.2 规范顺序）；组先于工序、父组先于子组。
- workplan 嵌套 → Program 组树；setups → Geometry 组；tools → Tool 组；
  方法组按加工域约定建（§4.2）。
- 确定性：同 plan 两次 Build → 命令序列字节级相同（§3.1e 镜像；PlanComparer 对齐基石）。

### 3.3 继承语义（不伪造值）

plan 缺字段 → 不产生 Set 命令；工序参数集 == plan 出现字段 ∩ ParamRegistry。
能力探测不支持的字段 → 跳过 + warning（与导出侧镜像）。

### 3.4 前置条件与处置

| # | 条件 | 不满足时的行为 |
|:---|:---|:---|
| 1 | plan 非 null | 抛 BuildAbortedException，终止 |
| 2 | operations 非空 | 抛 BuildAbortedException，终止（镜像导出侧前置 2） |
| 3 | workplan.root 存在 | 抛 BuildAbortedException，终止（schema 必填，防御性） |
| 4 | 引用闭合：tool_ref/setup_ref/feature_ref/operation_ref/workingstep_ref 均指向 plan 内实体 | 悬空 → error + 跳过该条目（工序/工步/叶子），其余继续 |
| 5 | operation_type 可映射（≠other，或 other 且 nx_template.type 非空） | 否则 error + 跳过该工序（近似工序口径，nx-plugin-design.md §6） |

### 3.5 后置条件

| # | 条件 | 校验方式 |
|:---|:---|:---|
| 1 | 命令序列满足 §3.2 规范顺序与确定性 | 测试锁定 |
| 2 | 模拟 S′ 满足 §3.1 round-trip | 测试锁定 |
| 3 | 所有跳过/降级行为均有 diagnostics 条目（绝不静默省略） | 测试锁定 |
| 4 | 输入 plan 不被修改（只读） | 结构保证（纯函数） |

## 4. 算法

### 4.1 总流程

```
Build(plan, profile):
 1. 前置检查（§3.4-1/2/3）                     → verify: 致命抛异常
 2. 引用闭合 + 生存判定（§3.4-4/5，逐条）       → verify: 悬空条目全落 error + 跳过
 3. 组命令生成（方法/刀具/几何/Program）        → verify: §2.2 规范顺序
 4. 工序命令生成（workplan 叶子序，参数映射）    → verify: §3.3 继承语义
 5. 模拟 S′                                     → verify: §3.1（测试侧）
 6. 诊断入库                                     → verify: §3.5-3
输出：BuildResult { Commands, Simulated, Diagnostics }
```

### 4.2 方法组约定（决策点 a，已拍板：不改合同）

按幸存工序加工域、首次出现序建默认组，命名表：

| 域 | 组名 | 域 | 组名 |
|:---|:---|:---|:---|
| MILLING | MILL_ROUGH | WEDM | WEDM_METHOD |
| DRILLING | DRILL_METHOD | ADDITIVE | ADDITIVE_METHOD |
| TURNING | TURN_METHOD | PROBING | PROBE_METHOD |
| MULTI_AXIS | MULTI_AXIS_METHOD | MACHINE_CONTROL | MACHINE_METHOD |
| USER_DEFINED | USER_METHOD | UNKNOWN（other+nx_template） | METHOD |

> PlanComparer 对比方法组维度时按此表归一（写入其口径）。

### 4.3 类型与参数映射

- operation_type → (typeName, domain)：TypeMapper 反向表（first-wins：每个 operation_type
  取 §4.2 全表中声明序第一个 typeName，如 bore→BORING、probe→ON_MACHINE_PROBING）；
  "other" → 用 nx_template.type 直落（域 UNKNOWN）。
- 参数：ParamRegistry 顺序遍历，plan strategy/technology 出现的字段 → SetParam（值直通，
  不换算）；能力不支持的字段跳过 + warning。
- 刀具/MCS 字段直填（§4.5/§4.7 口径，与导出侧同源）。

### 4.4 模拟器（Simulate）

按命令序列维护内存状态：组树（名字唯一查找）、四视图列表、工序集合、合成面
（每工序按其 feature.anchor_point 生成 FaceSnapshot，Centroid=anchor_point，
FaceType="Synthetic"）。PartName=plan.name，InputRef=plan.input_ref，
TemplateDefaults 为空——保证导出侧继承链解析结果 == plan 字段集（§3.1b 的机制来源）。

## 5. 风险与边界

| 风险 | 处置 |
|:---|:---|
| 方法组结构缺失（§2.1 注） | 约定默认组（§4.2），差异由 PlanComparer 归一 |
| 近似工序 | other + nx_template 直落；无 nx_template → error 跳过（§3.4-5） |
| 几何兜底 | MVP 用 anchor_point 合成面；face_ids 到位后 FaceResolver 精确匹配升级 |
| 版本参数 | CapabilityProfile 镜像处置（跳过 + warning） |
| typeName 的 NX 侧合法性 | Core 无法检测（无 NX 枚举），适配层执行时失败上报——风险登记，NX 集成验证覆盖 |
| workplan 叶子与 workingsteps 不一致 | 各自按引用闭合处置（§3.4-4），不一致即 error + 跳过 |

## 6. 实施顺序（dev-pattern 阶段⑤）

1. 命令模型 + 确定性骨架 → 2. 组树/刀具/setup 还原 → 3. 参数映射 → 4. 逐工序命令 +
继承语义 → 5. 模拟器（round-trip 最后实现但回归覆盖前四层） → 6. 前置/诊断收口。
