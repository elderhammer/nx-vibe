# NX 适配层分析 — 数据结构 / 性质 / 验证计划

> 更新时间：2026-09-03（M2 执行侧 / M3 完整闭环 GUI 会话实测达标）
> 分析对象：nx-plugin-design.md §4 定义的 NX 适配层 ×2 + 插件宿主（三步闭环第①②步的
> NX 侧桥接）
> 前置阅读：[nx-plugin-design.md](./nx-plugin-design.md)、[nxopen-research.md](./nxopen-research.md)、
> [plan-exporter.md](./plan-exporter.md)、[plan-executor.md](./plan-executor.md)、
> [dev-pattern.md](./dev-pattern.md)
>
> 定位：**两个薄壳**——Core 已把契约面全部定死（输入 `CamSetupSnapshot`、输出
> `RebuildCommand` 序列），适配层没有算法决策，只有「检测」（NX 对象图 → 快照 +
> 能力画像）与「执行」（命令序列 → NXOpen 调用）。
> dev-pattern §5 明确定位：适配层**不做单测**（写不出性质的面积靠隔离切分压小），
> 性质以**断言形式落在 NX 内 Journal 闭环**里验证（[nx-journal-manual-verification.md](./nx-journal-manual-verification.md)）。

---

## 1. 职责与边界

```
NX prj（手编工程，含 CAMSetup）           plan.json
    └─ 导出适配层：对象图 → CamSetupSnapshot ─▶ PlanExporter.Core ─▶ 序列化
    └─ 能力探测：版本/许可 → CapabilityProfile ─▶ 同上（前置条件 3/5 的检测源）

plan.json ─▶ PlanExecutor.Core ─▶ RebuildCommand[] ─▶ 执行适配层 ─▶ NX 建 prj′
    （命令序列 = RebuildSimulator 语义基准，适配层执行必须与其同构）

prj′ ─▶ 导出适配层 ─▶ plan″ ─▶ PlanComparer.Core ─▶ 偏差报告
```

- **检测 vs 处置分离**（dev-pattern §3 适配层教训）：导出适配层只读 NX 真实状态
  （生效值/许可/版本能力/几何属性），所有处置（跳过/降级/诊断）由 Core 的前置条件与
  拍平逻辑负责——适配层不决定「读不到怎么办」，只如实上报「读到了什么」。
- **会话只读**：导出侧只 `Get()`，不 `Set()`、不 `Commit()`，Builder 用毕 `Destroy()`
  （Core I1 在真实 NX 的执行者）。
- **执行忠实**：执行侧只做命令落点，不补值、不重排——命令里没有的字段**不产生任何
  Set 调用**（继承语义在 NX 侧自然生效，plan-executor.md §3.3）。

## 2. 数据结构（两侧映射面）

### 2.1 导出侧：NX 对象图 → CamSetupSnapshot（六项职责）

| # | 快照字段 | NX API 来源（nxopen-research §3） |
| :--- | :--- | :--- |
| 1 | ProgramRoot 组树（前序） | `ProgramOrderView` 根组递归 + 组内 Operation 序 |
| 2 | Method/Tool/Geometry 组表 | 三视图根组 + 组级 Builder 回读（MCS/刀具/方法组默认值） |
| 3 | OperationSnapshot（四父组引用） | `CAMOperationCollection` + `getParent(CAMSetup.View)` |
| 4 | OpParam（IsSet + Value） | 各 `createXxxBuilder(op)` 的 `Get()`——**直读生效值**（决策 D-适配-1） |
| 5 | TemplateDefaults | 模板根默认值（与 #4 同策略） |
| 6 | Faces/Edges + GeometryTags | 关联几何 Tag + UF 几何查询（`UF_MODL_ask_face_data/area`、`AskFaceNormals`） |

### 2.2 执行侧：RebuildCommand 序列 → NXOpen 调用

| 命令 | NXOpen 落点 |
| :--- | :--- |
| CreateCamSetup | `Part.CreateCamSetup()`（重建载体为另存副本，D-适配-3） |
| CreateMethodGroup | `MethodView.Root.CreateMethod(name, name)` |
| CreateToolGroup | `MachineToolView.Root.CreateTool(name, name)` + 刀具 Builder 按 `type` 分派 |
| CreateGeometryGroup | `GeometryView.Root.CreateGeometry` + `MillOrientGeomBuilder`（MCS/安全平面/夹具偏置） |
| CreateProgramGroup | `ProgramOrderView.Root.CreateProgram`（嵌套能力 M0 验证） |
| CreateOperation | `ops.Create(四父组, typeName, subtypeName, …)` → 对应 Builder → 逐 SetParam → Commit/Destroy |

**参数映射表**（执行侧最大代码量，隔离轴 4 的适配层版本）：`plan 字段名 → Builder
属性访问器`，落点全表见 nxopen-research §4.3/§4.4，扩表不改码。

### 2.3 宿主：三步闭环 Journal 入口

```
M3_Loop.vb：① 导出 prj → plan.json（schema 校验）→ ② 另存副本重建（命令执行）
            → ③ 导出 prj′ → plan″ → Compare(plan, plan″) → report.json
```

## 3. 性质（NX 内断言清单，不写单测）

| 性质 | 内容 | 断言位置 |
| :--- | :--- | :--- |
| 确定性 | 同一工程两次导出 → plan.json 字节级相同；集合枚举序/Tag 序/锚点选择序全部固定 | M1 闭环内断言 |
| 保序 | 快照 ProgramRoot 前序 = Program 视图刀路输出序（Core §3.1c 的输入保证） | M1 |
| 双射覆盖 | 每个 Operation ↔ 恰好一个快照工序 + 一个 plan 条目；无孤儿、无重复 | M1 |
| 会话只读 | 导出前后 prt 无持久变更、零遗留 Builder（Core I1 实际执行者） | M1 |
| 生效值直读 | OpParam 全量 IsSet=true（D-适配-1）；组参数表完整（刀具/MCS 组装原料） | M1 |
| 检测不处置 | 能力/许可探测只产 CapabilityProfile；Core 处置后 diagnostics 与跳过行为与 Core 测试锁定的一致 | M1 |
| 执行忠实 | 命令序列全量执行无异常；缺字段零 Set 调用；重建后可回读 | M2 |
| 闭环同构 | Compare(plan, plan″) 与 Core 零基线同构——偏差只应来自 NX 侧噪声，且每条可归因 | M3 |

## 4. 算法（遍历与映射，无决策）

### 4.1 导出遍历

```
1. 会话/Work Part 检查（前置条件 1 的检测部分）→ 失败即中止（Core 抛 ExportAborted）
2. 四视图遍历：Method/Tool/Geometry 组表先行（组参数 = 组级 Builder 直读）
3. Program 树前序 → workplan 骨架 + 组内工序序
4. 逐 Operation：getParent 四父组 → typeName/subtypeName → 参数表逐项 Get()（直读）
   → 关联几何 Tag 集（序固定）→ UF 几何查询产 Face/Edge 快照
5. 能力探测：逐参数版本检查 + 加工域许可检查 → CapabilityProfile
输出：CamSetupSnapshot + CapabilityProfile
```

### 4.2 执行映射

按命令序列原序执行（Core 已保证规范顺序：CamSetup → 方法 → 刀具 → 几何 →
Program 前序 → 工序叶子序）；每命令 try/finally 保证 Builder Destroy。

### 4.3 复杂度与确定性

| 项 | 量级 | 说明 |
| :--- | :--- | :--- |
| 时间 | O(Σ_ops·P + G) | P=参数数，G=关联几何面/边数（UF 查询 O(1)/面） |
| 确定性 | 完全确定 | 固定遍历序 + 无随机源；M1 字节级一致断言锁死 |

## 5. 决策记录（已定案）

| # | 决策点 | 定案 |
| :--- | :--- | :--- |
| D-适配-1 | 继承探测策略 | **直读生效值**（IsSet=true 全量）——plan-exporter.md 口径即"必须回读生效值"；继承态探测待 M0 确认 API 后作增强 |
| D-适配-2 | 重建几何关联 | **MVP 不关联**（几何维度偏差显形为已知预期；FaceResolver 到位后升级）——2026-09-03 定案补注：M4 探针全链实证 NX2406 mill_planar 域工序/组几何为 CAM 内部模型（特征/Ci/ScCollector 驱动），裸 Tag 面/体不向 `CAM.Geometry.GetItems` 物化（工序级/组级、Add/Commit/InitializeData 后恒空）→ 工序级面合同在本域按 known-skip 收口，面级几何归 3D 域后置（见核对清单 M4 节实证链） |
| D-适配-3 | 重建载体 | **prj 另存副本**（几何同源、零 STEP 导入风险；STEP 打开后置，nx-plugin-design §4） |
| D-适配-4 | 宿主形态 | **Journal 先行**（批处理 run_journal，无 add-in 注册/版本戳），INXAddIn 后置 |
| D-适配-5 | 参数映射表落点 | 适配层数据表（plan 字段 → Builder 属性访问器），与 Core ParamRegistry 对称 |
| D-适配-6 | NX SDK 依赖与构建 | 适配层引用 NXOpen DLL（本机 NX2406 已装，可编译）；Core 解决方案保持独立可构建可测试 |

## 6. 验证计划（M0-M3，逐里程碑）

| 里程碑 | 内容 | 状态（2026-09-02） |
| :--- | :--- | :--- |
| M0 | API 预研 journal（5 个未知点，见核对清单） | ✅ 5 点全答 + 发现批处理对象模板限制（详见核对清单） |
| M1 | 导出侧适配器 | ✅ 批处理实测：schema 0 错、两次导出字节一致、15 工序/4 刀具/1 setup 双射覆盖、无 ERROR（公制模板） |
| M2 | 执行侧适配器 | ✅ GUI 会话实测（2026-09-03）：28/28 命令全执行、prj′ 落盘回读一致、technology 98/98 匹配、MCS 反射落点正确（mcs 1/1 偏差 0）。调试链见核对清单 M2 节（模板键语义 / find-or-create / 根组名解析 / 枚举宽松匹配） |
| M3 | 最小闭环 | ✅ **真实偏差归零（2026-09-03 终态）**：导出→重建→再导出→Compare 全链跑通；structure=1.0 + tool 65/65 匹配 + parameter 全匹配 = plan 合同无歧义重建实证；deviations（WARNING）= 0，仅 7 条 known_skip（INFO，NX 写保护结构化豁免）。收敛历程见核对清单 M3 节（19→23→18→12→7→0，全程无 Core 缺陷） |
| M4 | 几何维度 + STEP 跨件 | ⏳ 几何维度**定案 known-skip**（2026-09-03 写探针 v1-v5 实证：2.5D 域几何不裸化，工序级/组级全空——实证链见核对清单 M4 节）；STEP 跨件闭环（M4c，结构/刀具/参数/策略/MCS 维度）待做 |

## 7. 风险与边界

| 风险 | 处置 |
| :--- | :--- |
| Builder Get() 语义不明（最大未知） | M0 已答：Value 直读生效值 + InheritanceStatus 可探测（留作增强，MVP 按 D-适配-1 直读全量 IsSet=true） |
| 嵌套 Program 组 API 能力 | M0 已答：CreateProgram(parent, …) 原生支持 |
| Inheritable 在真实 NX 的差异 | M3 偏差显形并归因，不静默 |
| 许可/版本差异 | CapabilityProfile 探测（MVP 返空画像 + 登记；逐参数版本探测后补） |
| 模板注册表仅真 GUI 会话加载（M0 + 2026-09-03 三态实测） | 批处理与 run_journal 交互会话均缺组/工序 subtype 注册（Create 必失败「需要的模板不存在」，打开 CAM 零件预热无效）；导出侧纯读不受影响；执行侧须在用户已进入加工环境的真会话跑**编译入口 EXE**（vbc 编 VB 装载器 → File → Execute → NX Open，核对清单 M2 节） |
| Create* 键语义 + 模板默认组（2026-09-03 实测） | 键 = (setup 族, subtype)（如 `CreateMethod(parent,"mill_planar","MILL_METHOD",…)`），旧式 typeName 全失效——executor 键表化见 NxTemplateKeys（含 plan 导出类型→mill_planar 族 Operation subtype 反向映射，探针 M2_Probe2 ③ 实测建表）；GUI 会话新 setup 自带模板默认组（PROGRAM/MILL_ROUGH/MCS_MAIN…）→ 组命令 find-or-create 复用；plan 根/约定组名 == 视图根组名（NC_PROGRAM/METHOD）→ 根组即目标组 |
| NX 写保护字段（Facing/EdgeChamfer 的 stock 类） | ✅ 已处置（2026-09-03）：E 段一段式/两段式对照实证 Commit 必回滚——重建值由 NX 模板固化，plan 无法驱动。NxWriteProtection 表（plan 类型×字段）+ 执行侧跳过 + 比较侧 UnwritableByPlanType 结构化豁免（known_skip INFO，绝不静默） |
| plan 工具合同表达力（模板工件/空参数刀具组） | ✅ 已处置（2026-09-03）：schema 加 tool.name（导出组名 → 重建 find-or-create 复用同名模板组，两侧同构）；MILL_USER_DEFINED 成形刀经 MillFormToolBuilder 补读参数（type 近似 END_MILL + warning，diameter 读 HelicalDiameter——G 段实测）；程序化 Create 的刀具重定向（PLANAR_MILL 挂 FormMill 被 NX 强制改挂 MILL）经「带参数 MILL 刀落地」消解——tool 维度 65/65 匹配 0 偏差 |
| NX 程序化 Create 语义边界（G3 实测） | PLANAR_MILL 类工序挂 FormMill 刀无条件重定向 MILL（与刀有无参数无关；原件 UI 手工可达、程序化不可达）——重建侧以带参数 MILL 刀落地（近似口径，导出侧 warning 显形），勿尝试程序化复刻 UI 状态 |
