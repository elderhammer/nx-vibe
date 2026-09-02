# PlanComparer 分析 — 数据结构 / 性质 / 算法

> 更新时间：2026-09-02
> 分析对象：nx-plugin-design.md §2.2 定义的 `PlanComparer` 模块（三步闭环第③步）
> 前置阅读：[nx-plugin-design.md](./nx-plugin-design.md)、[plan-exporter.md](./plan-exporter.md)、
> [plan-executor.md](./plan-executor.md)、[dev-pattern.md](./dev-pattern.md)（本模块按此模式开发）
>
> 定位：闭环的「差距量化」器。**纯函数 plan×plan → 偏差报告，零 NX**——
> 真实闭环中 prj′ vs prj 的对比 = `Export(prj)` × `Export(prj′)` 两个 plan 的对比，
> 两侧复用已建导出适配层，本模块 Core 无适配层。对比得以成立的前提（确定性、
> 顺序单调、条目独立）已由导出器/执行器的性质文档与测试固化。

---

## 1. 职责与边界

```
plan（ground truth 导出）  ┐
                          ├─ PlanComparePipeline.Compare(left, right, context)
plan′（重建后再导出）     ┘        → ComparisonReport（逐工序偏差表 + 汇总评分）
context = { rightCapability }   （已知跳过分类依据，§3.8）
```

- **只读 plan，不读 NX**：输入是 schema 校验过的两个 PlanRoot 对象图，输出是报告对象图。
- **不重导、不重建**：plan×plan 之外的一切（NX 回读、刀路生成）不属于本模块。
- **已知跳过 ≠ 偏差**（§3.8）：重建侧因能力画像跳过的参数，记 info 而非偏差——
  否则幽灵偏差使闭环失真（dev-pattern 决策点 D6）。
- **刀路维度不纳入 MVP**（§5，决策点 D5）：刀路时间/长度/过切只存在于 NX 侧，
  plan 无此字段；报告 schema 预留可空通道。

## 2. 数据结构

### 2.1 输入：PlanRoot × 2 + CompareContext

```
left  = ground truth plan（prj 导出）    right = 重建 plan（prj′ 导出）
context = { RightCapability: CapabilityProfile }   # 重建侧能力画像（D6 依据），可空 = 全能力
```

引用关系沿用 plan 合同（plan-exporter.md §2.3）：workplan 叶子 → workingstep →
operation → tool；workingstep → feature → geometry_ref。两个 plan 各自自洽。

### 2.2 对齐中间结构

| 结构 | 内容 | 生命周期 |
| :--- | :--- | :--- |
| 叶子投影 | workplan 前序叶子序列（每叶 = workingstep → operation → feature 引用链解析结果） | 对齐期 |
| 工序配对表 | left 工序 ↔ right 工序 的配对（`OpPair`），配对的键 = 类型键（operation_type + nx_template.type，§4.2） | 对齐期 → 维度对比 |
| 组树对比结果 | workplan 组节点的增/删/改名/移动行 | 对齐期 → 报告 |

### 2.3 输出：ComparisonReport（合同 = autocam-compare-report.schema.json）

```
ComparisonReport
├── report_id              "cmp-{left.plan_id}-{right.plan_id}"（确定性，§3.6）
├── left / right           { plan_id, name, input_ref }（两侧来源标识）
├── deviations[]           偏差行（只含非一致项；§3.11-2 绝不静默）
│    { dimension, operation_ref?, field?, kind, severity,
│      left?, right?, delta?, tolerance?, detail }
│    dimension: structure|tool|parameter|strategy|mcs|geometry（toolpath 预留）
│    kind: deviation|missing|extra|type_mismatch|order_swap|known_skip|unaligned|other
├── summary                 分维度汇总计数（匹配数/总数/漏/多/类型错/逆序/组差异…）
├── scores                  { structure_consistency, param_deviation_mean, geometry_match_rate }
├── toolpath                null（D5 预留通道，后续适配层注入刀路统计）
└── diagnostics[]           比较过程自身的诊断（DiagnosticEntry 形状，与 plan 合同同款）
```

评分定义（§4.6，公式写死进文档，测试锁定）：

- `structure_consistency = matched_ops / max(|L_ops|, |R_ops|)`，0/0 → 1.0（空对空视为一致）
- `param_deviation_mean` = 全部配对**容差口径数值字段**（AbsoluteMm/RelativePercent，
  覆盖 strategy + technology + 刀具）的相对偏差均值，单字段 `r = |Δ| / max(|L|, |R|)`
  （双零 → 0）；无此类字段 → 0。0 = 完全一致（Exact/VectorMm 口径不计入——整数编号/
  向量距离无相对偏差语义）
- `geometry_match_rate = matched_features / max(|L_features|, |R_features|)`，0/0 → 1.0

三个评分分母都取 max，保证 §3.2 对称性（左右互换评分不变）。

## 3. 性质

### 3.1 自反性

`Compare(P, P)` = 零偏差报告：deviations 为空、三评分全 1.0/0.0、无 error 诊断。
**自反性是本模块的验收基线**——闭环比对「差在哪」之前，先证明「同则不差」。

### 3.2 对称性

对任意 (A, B)：
- 有符号差互换取负：`delta(A,B) = -delta(B,A)`；`|delta|` 与容差判定相等；
- `missing` ↔ `extra` 互换，匹配数相等，类型错/逆序行互换方向；
- 评分对称（分母取 max 的机制保证）：`scores(A,B) = scores(B,A)`。

### 3.3 对齐保真

前提：导出器 §3.1b 条目独立性 + §3.1c 顺序单调（workplan 是 Program 树前序保序投影）。

- **纯置换**（左右类型多重集相等）：全部工序配对成功，仅产生 order_swap 行，
  参数/刀具/几何维度零幽灵偏差；
- **插入/删除**：新增/删除工序只产生对应 extra/missing 行，其余配对与字段比较不受影响；
- **类型替换**（同位置无法配对的双方）：产生 type_mismatch 行，不对其做字段比较；
- **配对纪律**：只有配对成功的工序才做字段级比较；未配对工序绝不产生参数偏差行。

### 3.4 容差边界（含入语义）

数值字段 `|Δ| ≤ tol` → match（压线算一致）；`|Δ| > tol` → deviation。
容差口径（决策点 D4，数据表 ToleranceRegistry，扩表不改码）：

| 口径 | 字段（示例） | 值 |
| :--- | :--- | :--- |
| AbsoluteMm | depth_per_cut / floor_stock / 刀具直径 / safe_plane_z 等线性尺寸 | 0.01 mm |
| RelativePercent | spindle_rpm / feed_cut.value / retract_speed 等转速进给 | 5% |
| VectorMm（欧氏距离） | mcs.origin / z_axis / x_axis / anchor_point | 0.01 mm |
| Exact | 枚举（cut_pattern/cycle/coolant…）、整数（num_flutes/fixture_offset/步距百分比）、mode 子字段 | 相等 |

**未入表字段：严格相等 + warning 诊断**（保守默认，绝不静默放行未定义口径）。

### 3.5 缩放同态（单位口径锁）

双方所有数值同乘 k（k>0）：AbsoluteMm/VectorMm 的 |Δ| 乘 k；RelativePercent 的
r 不变；判定结果（match/deviation）不变；param_deviation_mean 中绝对口径字段
贡献不变、相对口径字段贡献不变。锁死「全程 mm/rpm 口径、不做单位换算」的口径。

### 3.6 确定性

同输入两次 Compare → 报告字节级相同（序列化属性字母序 + 行序固定 + 无时间/随机源，
沿用 PlanSerializer 的 OrderedSnakeCaseContractResolver）。确定性是「对比可复现、
报告可回归」的必要条件。

### 3.7 单调性（局部扰动局部显现）

对 plan 做单字段扰动（改一个参数值/一个锚点/删一个刀具字段）：
- 只新增恰好对应该字段的偏差行；其余偏差行与评分其余分量不变；
- 修复任一偏差行（把对应字段改回一致）→ 各评分不降（I2）。

### 3.8 已知跳过 ≠ 偏差（D6）

right 侧缺失某字段且该字段 ∈ `context.RightCapability.UnsupportedParams`
→ kind = known_skip（severity = info，不计入偏差评分）；
不在 UnsupportedParams 的缺失 → 正常 deviation/missing。
分类依据是能力画像（结构化），**不解析自由文本诊断**。left 侧为 ground truth
权威：left 缺失 = missing、right 多出 = extra（无跳过豁免）。

### 3.9 零基线（元性质，闭环预演）

设 plan 为完整合法输入，`S′ = Simulate(Build(plan, cap))`，`plan′ = Export(S′, cap)`，
则 `Compare(plan, plan′, {cap})`：
- `cap` 全能力：零偏差报告（自反性的跨模块版本——三模块串联的自洽性）；
- `cap` 受限：仅 known_skip 行，无 deviation/missing 行。

这是「plan 合同能否无歧义重建」在 Core 内零部署的预演：本模块测得的一切偏差
只可能来自 NX 侧保真度，而非 Core 自身。

### 3.10 前置条件

| # | 条件 | 不满足时的行为 |
| :--- | :--- | :--- |
| 1 | left/right 非 null | 抛 CompareAbortedException，终止 |
| 2 | 两侧 workplan.root 存在 | 抛 CompareAbortedException，终止（对齐无权威序） |
| 3 | context 非 null（可空参数） | 缺省空画像（无已知跳过豁免） |
| 4 | 引用闭合：叶子/工步/工序/刀具/特征/setup 引用均指向 plan 内实体 | 悬空 → error 诊断 + 该条目 unaligned 行，其余继续（镜像执行器 §3.4-4） |
| 5 | 空 plan（无工序） | 合法：空对空 → 三评分 1.0/0.0/1.0；一侧空 → structure_consistency = 0 |

### 3.11 后置条件

| # | 条件 | 校验方式 |
| :--- | :--- | :--- |
| 1 | 报告通过 autocam-compare-report.schema.json 校验 | 报告 schema 校验器 |
| 2 | 绝不静默：所有非一致项显式成行（missing/extra/type_mismatch/order_swap/known_skip/unaligned 全覆盖），无法对齐的条目必有 unaligned 行 + 诊断 | 偏差行覆盖率检查 |
| 3 | 行序确定：维度固定序（structure→tool→parameter→strategy→mcs→geometry）→ 维度内按 left workplan 前序 → 同工序内按字段名字典序（Ordinal） | 测试锁定 |
| 4 | 评分满足 §4.6 公式与 §3.2 对称性 | 测试锁定 |
| 5 | 输入 plans 不被修改（纯函数） | 结构保证 + 测试锁定 |

### 3.12 不变式

| 不变性 | 内容 | 维护点 |
| :--- | :--- | :--- |
| I1 行序确定 | 任意中间状态的行序都是 §3.11-3 规则的部分结果 | 各比较器按固定序遍历 |
| I2 评分单调 | 修复偏差行 → 评分不降；评分只由比较结果聚合而来 | 评分聚合器 |
| I3 跳过降级 | known_skip 只产生 info，不产生 warning/error | 已知跳过判定 |
| I4 对称口径 | 全部容差判定用 |Δ|，评分分母取 max | 容差引擎 + 评分 |
| I5 单位一致 | 全程 mm/rpm 口径，数值比较不做单位换算 | 容差表 |

## 4. 算法

### 4.1 总流程

```
Compare(left, right, context):
 1. 前置检查（§3.10-1/2）                     → verify: 致命抛异常
 2. 叶子投影 + 工序对齐（§4.2）+ 组树对比（§4.3）→ verify: 配对纪律（§3.3）
 3. 七维度对比（§4.5，容差引擎 §4.4）          → verify: 容差口径（§3.4）
 4. 评分聚合（§4.6）                          → verify: 公式 + 对称性（§3.2）
 5. 报告组装 + schema 校验（§4.7）             → verify: §3.11-1/2
输出：ComparisonReport
```

### 4.2 工序对齐（核心算法，配对纪律的机制）

```
1. 叶子投影：workplan 前序收集叶子（WorkingstepRef 非空），沿引用链解析出
   operation（悬空引用 → unaligned 行 + error 诊断，剔除出对齐）。
   类型键 key(op) = operation_type + "|" + nx_template.type。

2. 类型多重集相等（每种 key 左右计数相同）：
   → 实例序配对：每种 key 按出现序一一配对（全配成功）。
   → 逆序检测：配对左右位置不同 → 每工序一条 order_swap 行
     （field=position，left/right=1 基序号）。

3. 多重集不等：贪心配对——right 按序取最早可用同键 left（同键内实例可互换，
   匹配数 = Σ min(两侧各键计数) = LCS 最大长度；决胜 = right 序 + left 最早）。
   → 未配对残留：位置相同且双方均未配对 → type_mismatch 行（不再做字段比较）；
     其余 left 残留 → missing 行、right 残留 → extra 行。
```

复杂度 O(n·m)（贪心配对），配对结果确定。**配对表是七维度对比的唯一入口**——
未配对工序不可能进入字段比较（§3.3 配对纪律的结构性保证）。

### 4.3 组树对比（structure 维度组级部分）

workplan 组节点递归对比（位置+组名双键）：同位置同名 → 匹配组，递归子节点；
同位置异名 → group 差异行（field=组名）；一侧多出/缺少子树 → missing/extra 组行。
组级行 operation_ref 为空，与工序行同入 structure 维度。

### 4.4 容差引擎（ToleranceRegistry + 值比较器）

```
compare(left, right, specByPath):
  spec = ToleranceRegistry.Lookup(fieldPath)     # 未入表 → Exact + warning 诊断
  数值/数值     → 按 spec 口径判定（§3.4 表），产出 delta = right - left、tolerance
  枚举串/枚举串 → 相等判定（mode/类型串等，无论 spec 均 Exact）
  整数/整数     → Exact（num_flutes / fixture_offset / 步距百分比）
  向量/向量     → VectorMm 欧氏距离（mcs / anchor_point，元素级对齐）
  复合/复合     → 递归逐键（stepover{ mode, value } → path "stepover.mode"/"stepover.value"）
  类型不一致    → deviation 行（detail 注明类型差），不猜测
```

### 4.5 七维度对比（按配对表逐对进行）

| 维度 | 对比内容 | 行口径 |
| :--- | :--- | :--- |
| structure | 组树对比（§4.3）+ 对齐结果行（missing/extra/type_mismatch/order_swap） | operation_ref 指向工序（组级行为空） |
| tool | 配对工序经 tool_ref 解析的刀具逐字段（ParamRegistry.ToolFields）；刀具表计数差异 → missing/extra 行 | field = 刀具字段名 |
| parameter | technology 字典键并集：左独有 → missing、右独有 → extra/known_skip（§3.8）、共有 → 容差引擎 | field = 参数字段名 |
| strategy | strategy 字典同 parameter | 同上 |
| mcs | setups 按位置配对：origin/z_axis/x_axis（VectorMm）、safe_plane_z（AbsoluteMm）、fixture_offset（Exact）；计数差异 → missing/extra | operation_ref 为空（setup 级） |
| geometry | 配对工序的 feature.anchor_point：双侧有 → VectorMm 0.01；单侧 → missing/extra；左锚点 0.01 内命中 >1 个右锚点 → warning 诊断（对称碰撞，镜像导出器 §4.5） | field = anchor_point |

（toolpath 维度：预留，不比较。）

### 4.6 评分聚合

按 §2.3 公式从比较结果聚合：`matched_ops` 来自配对表；`param_deviation_mean`
遍历配对容差口径数值字段的 r（含一致字段 r=0，故修偏差必降均值——I2）；
`geometry_match_rate` 按配对特征数计。summary 各维度计数与 deviations 行一一可核对。

### 4.7 报告组装与校验

1. 组装 ComparisonReport（§2.3），report_id 确定性生成（§3.6）。
2. 比较过程自身诊断（悬空引用/未知口径/对称碰撞/无法对齐）落 report.diagnostics[]，
   输入两侧的 plan.diagnostics 留在各自 plan 内（报告页可同时取三份，不复制）。
3. 报告经 autocam-compare-report.schema.json 校验后返回（后置条件 1）。

### 4.8 复杂度与确定性

| 项 | 量级 | 说明 |
| :--- | :--- | :--- |
| 时间 | O(n·m + matched·(P+F)) | n/m=两侧工序数，P=字段数，F=锚点对数（对齐 O(n·m)，维度对比线性） |
| 空间 | O(report 规模) | 配对表 + 偏差行 + 汇总 |
| 确定性 | 完全确定 | 固定遍历序 + 贪心确定性决胜 + 无时间/随机（§3.6） |

## 5. 风险与边界

| 风险 | 处置 |
| :--- | :--- |
| 刀路维度（时间/长度/过切）无 plan 数据 | MVP 排除，schema 预留 toolpath 通道（D5） |
| 锚点对称碰撞（对称/阵列面） | warning 诊断提示人工复核（镜像导出器 §4.5），不强行配对 |
| 方法组维度 | plan 不含方法组结构（MVP 无 method_ref），plan×plan 无可比数据——不设维度；未来 plan 增强后按 MethodGroupNaming 归一（plan-executor.md §4.2 预留口径） |
| 未入容差表字段 | 严格相等 + warning（§3.4 保守默认），扩表即放开 |
| 大偏差集合 | LCS O(n·m) 在 MVP 工序规模（<100）下无压力 |
| 引用悬空 plan | 逐条目 error + unaligned，不终止（§3.10-4） |

## 6. 实施顺序（dev-pattern 阶段⑤，按合同风险排序）

1. 工序对齐 + 组树对比（配对纪律是其余维度的前提） → 2. 容差引擎（数值核心）
→ 3. 七维度比较器（表驱动） → 4. 评分聚合 → 5. 报告组装 + schema 校验
→ 6. 管线入口 + 前置条件 + 已知跳过 + 零基线收口。

## 7. 决策记录（dev-pattern 阶段①拍板，已定案）

| # | 决策点 | 定案 |
| :--- | :--- | :--- |
| D1 | 共享层抽取 | 抽 `Autocam.Plan.Core`：PlanModel / CamSetupSnapshot / CapabilityProfile / DiagnosticsCollector / TypeMapper / ParamRegistry / MethodGroupNaming / Serialization（rule of three 第 3 消费者触发，src/README 预留动作） |
| D2 | 对齐键 | 位置归一双键（workplan 前序 + 类型键），不匹配 ID（ID 为不透明串，格式不作合同） |
| D3 | 报告合同 | 立 `schema/autocam-compare-report.schema.json`（draft-07，与 plan schema 同治理、同测试资产锁定） |
| D4 | 容差表 | 线性尺寸 0.01mm 绝对；转速/进给相对 5%；枚举/整数精确；向量欧氏 0.01mm；未入表 → 严格相等 + warning |
| D5 | 刀路维度 | MVP 不纳入，schema 预留可空 toolpath 通道 |
| D6 | 已知跳过 | 按 CapabilityProfile 结构化判定（不解析自由文本诊断） |
| D7 | 聚合粒度 | 逐条偏差行 + 汇总评分双粒度（写测试前定死） |
