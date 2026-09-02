# PlanExporter 分析 — 数据结构 / 性质 / 算法

> 更新时间：2026-09-02
> 分析对象：nx-plugin-design.md §2.1 定义的 `PlanExporter` 模块（导出侧核心）
> 前置阅读：[nx-plugin-design.md](./nx-plugin-design.md)、[nxopen-research.md](./nxopen-research.md)
>
> 本文从三个视角分析 PlanExporter：**数据结构**（输入/中间/输出）、**性质**
> （单调性、前置条件、后置条件、不变性）、**算法**（各阶段流程与复杂度）。
> 性质部分是本文重点——它们是 PlanComparer 正确性与「导出→重建→对比」闭环可信度的依据。

---

## 1. 职责与边界

PlanExporter 位于三步闭环的**第①步（ground truth 采集）**：

```
工程师手编 NX prj（含完整 CAMSetup：程序组/刀具/几何/MCS/工序）
  └─ PlanExporter：遍历四视图组树 + 每个 Operation
                  回读各 Builder 实际生效参数
                  序列化 plan.json（工程师工艺意图的数字化存档）
```

它只负责**读取与序列化**，不创建、不修改任何 NX 对象。plan.json 的合同
（autocam-plan.schema.json v3）同时被第②步 PlanExecutor（重建）与第③步
PlanComparer（对比）消费，因此导出结果的质量直接决定闭环是否可信。

**关键口径（来自 nx-plugin-design.md §2.1 警告框）**：NX Builder 参数未显式设置时
继承父组/方法组默认值（Inheritable 语义）。导出必须回读**生效值（resolved value）**
而非仅显式值，否则 plan 缺字段，第②步重建与 ground truth 必然不一致。

---

## 2. 数据结构

### 2.1 输入侧：NX 工程对象图（nxopen-research.md §3.1）

```
Part.CAMSetup
├── ProgramOrderView.Root   ── NCGroup 树（Program 组，决定刀路输出顺序）
├── MachineToolView.Root    ── NCGroup 树（Tool 组 + Machine 组）
├── GeometryView.Root       ── NCGroup 树（MCS / WORKPIECE / PART / BLANK）
├── MethodView.Root         ── NCGroup 树（MILL_ROUGH / MILL_FINISH / DRILL_METHOD）
└── CAMOperationCollection  ── Operation[]，每个挂在四个父组之下
```

| 输入对象 | 关键读取点 | 用途 |
| :--- | :--- | :--- |
| `CAMSetup` | 四视图根组集合、`CAMOperationCollection` | 遍历入口 |
| `NCGroup`（Program） | 组名、父子关系、组内 Operation 顺序 | workplan 树序 / setup 划分 |
| `NCGroup`（Geometry） | `MillOrientGeomBuilder.mcs()`、`transferClearanceBuilder`、`fixtureOffsetBuilder` | `setups[].mcs` / 安全平面 / 夹具偏置 |
| `NCGroup`（Tool） | 刀具 Builder 全参数（直径/刃数/圆角/螺距…） | `resources.tools[]` |
| `NCGroup`（Method） | 组名 + 组级 Builder 默认值 | `method_ref` + 继承解析的上游 |
| `Operation` | `typeName`/`subtypeName`、`getParent(CAMSetup.View)`、关联几何 Tag | operation 条目 / 四父组引用 / geometry_ref |
| `Builder`（约 70 个 `createXxxBuilder`） | 各参数 `Get()`：显式值 vs 继承态 | strategy / technology 生效值 |
| 几何体（Face/Edge/Point） | `UF_MODL_ask_face_data/ask_face_area/AskFaceNormals`、`Edge.GetLength`、`UF_MODL_ask_edge_convexity` | 几何属性锚点 |

### 2.2 中间结构

| 结构 | 内容 | 生命周期 |
| :--- | :--- | :--- |
| 组树快照 | 四视图 NCGroup 树的内存镜像（组名、父子序、组参数生效值） | 遍历期 |
| Operation 回读缓存 | 每个 Operation 的：四父组引用、typeName/subtypeName、strategy/technology 拍平生效值、关联几何 Tag 集 | 遍历期 |
| 几何锚点表 | `NX Tag → (centroid, area, face_type, normal)` 属性元组（Face 口径），`(length, convexity, endpoints)`（Edge 口径） | 遍历期 |
| plan 对象图 | 强类型模型（对齐 schema v3，见 2.3） | 组装期 → 序列化 |

### 2.3 输出侧：plan.json 结构（MVP 清单，nx-plugin-design.md §5）

```
plan.json
├── plan_id / input_ref / name
├── setups[]          mcs{origin, z_axis, x_axis}, safe_plane_z, fixture_offset
├── resources.tools[] type, diameter, num_flutes, (flute_length), lower_corner_radius
├── features[]        feature_id, feature_type, geometry_ref, params
│   └── geometry_ref  { face_ids?, edge_ids?, anchor_point: [x,y,z] }
├── operations[]      operation_id, operation_type, nx_template{type, subtype},
│                     tool_ref, strategy, technology
├── workingsteps[]    workingstep_id, feature_ref, operation_ref, setup_ref
├── workplan          { root, elements }（Program 组树的前序投影）
└── diagnostics[]     { level: info|warning|error, ... }
```

引用关系（导出结果必须自洽，见 §3.4）：

```
workingstep ──operation_ref──▶ operation ──tool_ref──▶ tool
     │              ▲                                   │
     └─feature_ref─▶ feature ◀──geometry_ref── 几何锚点（云端 STEP 侧填 face_ids）
```

> ⚠️ 口径说明：`face_ids`/`edge_ids` 是 OCCT 遍历 ID，属**原始 STEP 文件**（云端
> geometry.json）的标识体系；手编 NX 工程不持有 OCCT ID。因此**导出侧只产出属性锚点
> （质心+面积+类型+法向）与 `anchor_point` 兜底**，`face_ids` 由云端 STEP 侧按锚点匹配
> 后回填（与导入侧 FaceResolver 同一条匹配算法，方向相反）。这是对
> nx-plugin-design.md §2.1「按 NX Tag → 几何属性锚点（与 FaceResolver 反向）」的直接
> 展开。

---

## 3. 性质

### 3.1 单调性

PlanExporter 的单调性在四个层面成立，前提均为「继承解析已拍平」：

**(a) 会话状态单调（导出是只读恒等映射）**
导出映射对 NX 工程状态是恒等：`State(Export(P)) = State(P)`。导出前后工程状态
（含未保存会话）不变。这是后续幂等性的基础。

**(b) 对象级单调扩展（增量导出）**
设工程 P ⊆ P′（P′ 在 P 基础上**仅新增**对象，不改已有对象及其继承来源，如新增一道
工序/一把刀具/一个 Program 组），则：

```
Export(P′) = Export(P) ⊕ {新增条目}
```

已有条目**逐字段相等**；workplan 为保序插入。关键推论：**导出条目间相互独立**——
一道工序的导出结果不依赖其他工序的存在，PlanComparer 可以逐工序对比而不必对齐全局
上下文。

**反例（说明拍平的必要性）**：若 P′ 修改了某个 Method 组的默认值（非新增，是对已有
继承来源的修改），则所有继承该组的工序条目都会变化——此时不满足 (b)。正因为导出
把生效值拍平固化，继承来源的后续修改不会静默改写历史导出结果；同时新增对象也不会
污染已有条目。

**(c) 顺序单调（保序嵌入）**
workplan 是 Program 组树**前序遍历的保序像**：Program 视图中 A 先于 B 输出 ⇒
workplan 中 `workingstep(A)` 先于 `workingstep(B)`；组树祖先-后代关系保持为 workplan
嵌套关系；新工序插入 Program 树的某位置 ⇒ workplan 在对应位置插入。该性质使
PlanComparer 的「结构对比」退化为序列对齐问题。

**(d) 信息量单调（回读覆盖）**
输出字段集 = MVP 必填集 ∪ 能力探测成功集；生效值 ⊇ 显式值，故导出结果信息量不小于
仅读显式值的方案。任何读不到的字段只**增补** diagnostics 条目，**不减**字段。

**(e) 确定性（幂等退化形式）**
同一工程多次导出结果一致（首次应用即不动点）：遍历顺序固定（四视图树序 + 操作集合
序）、锚点计算用 NX 精确 API 无随机性、ID 由确定计数生成。确定性是 PlanComparer 可
重复执行的必要条件。

### 3.2 前置条件

| # | 条件 | 不满足时的行为 |
| :--- | :--- | :--- |
| 1 | NX 会话存在，目标 prt 已打开且为 Work Part | 报 error，终止 |
| 2 | `Part.CAMSetup` 存在且含完整四视图组树与工序集合 | 报 error，终止（空 CAMSetup 无 ground truth 可导） |
| 3 | cam_base 许可可用；所涉加工域（车削/多轴/WEDM…）许可可用 | 许可缺失的工序报 error 并跳过该工序，其余继续 |
| 4 | 无进行中的编辑会话：不存在未 Commit 的 Builder | 报 warning，先于导出创建的遗留 Builder 不归导出器管理，需工程师确认 |
| 5 | NX 版本 ≥ 最低支持版本（2306+），逐参数能力探测通过（如 `bottomClearance` 需 2312+） | 探测失败的参数跳过 + warning，不静默填充 |
| 6 | 每个 Operation 经 `getParent(CAMSetup.View)` 能解析出四个父组 | 父组缺失的工序报 error 并跳过 |
| 7 | 每个 Operation 的 typeName 能创建对应 Builder（`createXxxBuilder` 成功） | 报 error 并跳过该工序（typeName 保留进 nx_template 兜底） |

### 3.3 后置条件

| # | 条件 | 校验方式 |
| :--- | :--- | :--- |
| 1 | plan.json 已写出，且通过 autocam-plan.schema.json v3 校验 | schema 校验器 |
| 2 | MVP 清单字段（§2.3）**必填输出**：缺项显式落 diagnostics[]，绝不静默省略 | 字段覆盖率检查 |
| 3 | 所有 strategy/technology 数值为**生效值**（resolved），而非仅显式值 | 导出期继承解析（§4.3） |
| 4 | 引用完整性：`tool_ref`/`operation_ref`/`feature_ref`/`setup_ref` 全部指向 plan 内实体，无悬空引用 | 引用闭合检查 |
| 5 | 覆盖双射：每个 Operation ↔ 恰好一个 operation 条目 + 一个 workingstep 条目；每个 Tool 组 ↔ 一个 tool 条目；无孤儿、无重复 | 集合计数 + 唯一性检查 |
| 6 | workplan 顺序 = Program 视图输出顺序（§3.1c） | 序一致性检查 |
| 7 | 会话无损：prt 无任何持久变更；导出期创建的临时 Builder 全部 Destroy、零 Commit | 会话状态对比（导出前后） |
| 8 | `anchor_point` 等数值在模型局部坐标，单位为 mm/rpm 口径 | 单位约定检查 |

> 元后置条件（闭环本身要验证的，导出器不保证）：plan 输入 PlanExecutor 后能重建出
> 与 ground truth 一致的工程。这正是第②③步存在的意义——后置条件 1-8 只保证「plan
> 忠实记录了意图」，不保证「合同无歧义」。

### 3.4 不变性

遍历全程持续成立的不变量：

| 不变性 | 内容 | 维护点 |
| :--- | :--- | :--- |
| I1 只读 | 任何时刻未对 CAMSetup 做持久化写：临时 Builder 只 `Get()`，不 `Set()`、不 `Commit()`，用毕 `Destroy()` | 每个 Builder 的 try/finally |
| I2 引用闭合 | 回读缓存中每条 Operation 记录的四个父组引用、tool_ref 均已解析并指向已入缓存的组对象（Tool/Geometry/Method 组先于 Operation 填充） | 两遍遍历（先组后工序） |
| I3 ID 唯一 | operation_id / workingstep_id / feature_id / tool id 全局唯一且确定（单调递增计数器） | ID 分配器 |
| I4 序一致 | workplan 构建的任意中间状态都是 Program 树前序投影的保序部分结果 | 前序遍历顺序写 |
| I5 锚点精度 | 几何锚点与源面/边属性误差 ≤ 0.01mm 容差；计算走 NX 精确 API（无三角化误差） | 锚点提取（§4.5） |
| I6 单位一致 | 全程 mm / rpm / mm-min 口径，不做单位换算 | 单位约定 |
| I7 失败不破坏 | 单条工序导出失败只影响该条目 + diagnostics，不影响其余条目与已完成结果（对应 §3.1b 独立性） | 逐工序 try/catch 隔离 |

---

## 4. 算法

### 4.1 总流程

```
输入：手编 prt（Work Part）
 1. 前置检查        → verify: §3.2 条件 1-4（会话/CAMSetup/许可/编辑状态）
 2. 组树快照        → verify: 四视图树完整入缓存（I2 的前半）
     2a. Tool/Geometry/Method 组先行：回读组参数（MCS/刀具/方法组默认值）
     2b. Program 树前序遍历：workplan 骨架 + setup 划分
 3. 逐 Operation 回读 → verify: 每个工序产出 operation + workingstep 条目（§3.3-5 双射）
     3a. 生效值拍平（§4.3）  3b. 类型映射（§4.4）  3c. 几何锚点（§4.5）
 4. 组装 plan 对象图  → verify: 引用闭合（§3.3-4）+ ID 唯一（I3）+ 序一致（I4）
 5. schema 校验 + 序列化 → verify: 校验通过，缺项/异常全量进 diagnostics[]
 6. 会话无损确认     → verify: 无持久变更、零遗留 Builder（§3.3-7）
输出：plan.json
```

### 4.2 组树遍历与 setup 划分

```
procedure ExportGroups(camSetup):
  # 2a. 非 Program 视图：为继承解析与资源表铺底（I2）
  for view in {MachineToolView, GeometryView, MethodView}:
      walk(camSetup.<view>.Root, node => 快照(node, 组参数生效值))
  # 2b. Program 视图：workplan 骨架（保序，I4）
  workplan = PreorderWalk(camSetup.ProgramOrderView.Root,
                          node => WorkplanNode(name, children))
  setups = SplitByGroupName(workplan)     # 按 Program 组名约定还原 setup
```

- Program 组树**前序遍历**：组内 Operation 顺序即刀路输出顺序 → workplan 保序投影（§3.1c）。
- setup 划分按 Program 组名约定（如 `PROGRAM_1` → setup_1），MCS 从对应 Geometry 组回读。
- 组参数（MCS/安全平面/方法组默认值）回读走组级 Builder（`MillOrientGeomBuilder` 等），
  作为 §4.3 继承解析的上游缓存。

### 4.3 生效值回读（继承解析拍平，核心算法）

问题：Builder 参数未显式设置时呈**继承态**，`Get()` 读不到数值；必须沿
「Operation → 父 Method/Tool/Geometry 组 → 模板根」向上解析，取最近显式值。

```
procedure ReadResolved(operation):
  builder = camSetup.Create<Xxx>Builder(operation)   # 按 typeName 选 Builder
  try:
      for p in MVP_Params(builder):                  # strategy/technology 字段集
          v = builder.<p>.Get()
          if v is 继承态:
              v = ResolveUp(p, parentGroups(operation))   # 组缓存中查，逐级向上
              if v 仍不可解析: diagnostics += warning(p)  # 缺项只增诊断，不减字段（§3.1d）
          record[p] = v                              # 拍平为具体数值/枚举
  finally:
      builder.Destroy()                              # I1：零 Commit
```

- 继承链深度 ≤ 3（操作 → 组 → 模板根），组参数已在 §4.2 预缓存，故单参数解析 O(1) 查表。
- 枚举类参数（cut_pattern/cycle/…）拍平为 plan 枚举值；数值参数带单位语义（mm/rpm）。
- 该算法是 §3.1b 单调性的机制来源：生效值一旦拍平，条目间即相互独立。

### 4.4 操作类型映射

```
map(typeName, subtypeName):
  查映射表（nxopen-research.md §4.2 枚举表，如 CAVITY_MILL → mill_cavity）
  命中   → operation_type = 枚举值; nx_template = {type, subtype}
  未命中 → operation_type = "other"; nx_template 保留原始字符串; diagnostics += warning
```

未命中不猜测、不丢弃——原始 typeName 全量保留，保证第②步「近似工序按 nx_template
真实类型落地」有依据（nx-plugin-design.md §6）。

### 4.5 几何锚点提取（FaceResolver 反向，唯一需要匹配算法的环节）

```
procedure ExtractAnchors(operation):
  tags = 取关联几何（Builder Geometry / Feature.GetFaces / HoleBossGeom.holeList）
  for each Face tag:
      anchor = (centroid, area, face_type, normal)   # UF_MODL_ask_face_data / area / normals
      锚点表[tag] = anchor
  for each Edge tag:
      anchor = (length, convexity, endpoints)        # Edge.GetLength / ask_edge_convexity
  feature.anchor_point = 孔心（孔类）/ 质心（其余）   # 无面映射能力时的兜底
```

- 属性元组是云端 STEP 侧回填 `face_ids` 与导入侧 FaceResolver 的**共同匹配键**（容差 0.01mm）。
- **对称特征风险**：多个面可能锚点属性相同（对称/阵列面）→ 锚点表出现碰撞时 diagnostics
  标 warning 提示人工复核（nx-plugin-design.md §6）。
- 计算全部走 NX 精确 API，无三角化误差（I5）。

### 4.6 组参数回读（MCS / 安全平面 / 刀具）

| 源 | 回读 | plan 落点 |
| :--- | :--- | :--- |
| `MillOrientGeomBuilder.mcs()` | 原点 + Z 轴 + X 轴 | `setups[].mcs` |
| `transferClearanceBuilder` | 安全平面高度 | `setups[].safe_plane_z` |
| `fixtureOffsetBuilder` | G54/G55… | `setups[].fixture_offset` |
| 刀具 Builder（`MillingToolBuilder`/`DrillToolBuilder`） | 直径/刃数/刃长/底部圆角/子类型… | `resources.tools[]` |

刀具参数与 NX Builder 字段一一对应，是「最直填」的部分，无需派生计算。

### 4.7 组装、校验与序列化

1. 组装 plan 对象图（§2.3），逐条校验：schema v3 通过（§3.3-1）、引用闭合（§3.3-4）、
   双射覆盖（§3.3-5）、序一致（§3.3-6）、ID 唯一（I3）。
2. 全部异常/缺项/碰撞以 `{level, code, detail}` 落入 diagnostics[]。
3. 写出 plan.json（`plan_id` 确定性生成，保证 §3.1e 可复现对比）。

### 4.8 复杂度与确定性

| 项 | 量级 | 说明 |
| :--- | :--- | :--- |
| 时间 | O(Σ_ops · P · D + G) | P=每工序回读参数数，D=继承链深度（≤3），G=关联几何面/边数 |
| 空间 | O(plan 规模) | 组树快照 + 回读缓存 + 锚点表 + 输出对象图 |
| 确定性 | 完全确定 | 固定遍历序 + 精确 API + 确定 ID（§3.1e）；无随机源 |

---

## 5. 风险与边界（对齐 nx-plugin-design.md §6）

| 风险 | 本文对应分析 |
| :--- | :--- |
| 继承值捕获 | §4.3 生效值回读是正确性的核心；继承解析失败只降级为 warning 缺项，不伪造值 |
| 几何映射（跨 prt 无共享 Tag） | §2.3 口径 + §4.5：导出只产属性锚点，OCCT ID 由云端回填；对称碰撞标 diagnostic |
| 近似工序 | §4.4：导出按真实 typeName 落 nx_template，不对 FreeCAD 口径做 approximation |
| 版本差异 | §3.2-5 逐参数能力探测；读不到的版本新增参数跳过 + warning |
| 2.5D 边界 | 曲面/回转类超出 MVP 口径，导出时标 info 提示，不强行展开 |
