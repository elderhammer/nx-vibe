# NX Journal 验证清单（M0-M3 手动核对）

> 更新时间：2026-09-03（M2 执行侧 + M3 完整闭环 GUI 会话实测达标）
> 用途：适配层验证的核对清单（nx-adapter.md §6）。批处理验证（run_journal）与
> 宿主机 GUI 目视核对共用。被 nx-plugin-design.md §4 引用（此前缺失，本次补建）。
> 运行方式：`UGII_BATCH_MODE=1 "…\NXBIN\run_journal.exe" <journal.vb>`（SSH/批处理）；
> GUI 会话执行：VB 装载器经 vbc 编成 exe 后 File → Execute → NX Open（2026-09-03 实测：
> Execute 不收 .vb；run_journal 交互模式同样不加载加工模板注册表——见 M2 节运行方式修正）。
> 输出落 `C:\nx-vibe-journal-out\`。

---

## M0：API 预研（M0_*.vb，已完成 ✅）

> 结论（2026-09-02，NX2406 批处理实测 + 反射 dump，m0_*.txt 存档于 C:\nx-vibe-journal-out\）：

| # | 未知点 | 实测结论 |
| :--- | :--- | :--- |
| 1 | Builder `Get()` 语义 / 继承态探测 | `InheritableDoubleBuilder.Value` 直读生效值；**`InheritanceStatus` 属性可探测继承态**（D-适配-1 增强通道存在） |
| 2 | 嵌套 Program 组 | `NCGroupCollection.CreateProgram(parentGroup, typeName, …)` 带父组参数，嵌套原生支持 |
| 3 | 四父组回读 | **不用 `getParent(View)`**——`op.ParentProgramOrder/ParentMachineMethod/ParentMachineTool/ParentGeometry` 直接属性 |
| 4 | 刀具 Builder 分派 | `CreateMillToolBuilder`（铣）/ `CreateDrillStdToolBuilder`（钻）均可用；`TlDiameterBuilder/TlNumFlutesBuilder/TlLowCorRadBuilder` 齐全 |
| 5 | 枚举序稳定性 | `CAMOperationCollection` 两次枚举顺序一致 ✓ |
| 6 | **对象模板注册表（新发现）** | **组/工序模板仅真 GUI 会话加载**（2026-09-03 三态实测）：setup 族注册 ✓（17 个，批处理也有）；组/工序 `Create` 的 subtype 注册表只随「用户进入加工环境」加载——批处理与 run_journal 交互模式均缺（`SpecifyConfiguration`/`AddTemplateType`/`ApplicationSwitchImmediate` 无法补救，打开 CAM 零件预热亦无效）。Create* 键语义 = (setup 族, subtype)，见 M2 节 |

**关键 API 口径修正**（nxopen-research.md 多处过时，以实测为准）：
- 会话链路：`NewDisplay → SetWork → CreateCamSession → CreateCamSetup("mill_planar")`（缺一步都失败）
- 视图根：`camSetup.GetRoot(CAMSetup.View.ProgramOrder / MachineMethod / Geometry / MachineTool)`
- 工序创建：`CAMOperationCollection.Create(四父组, typeName, subtypeName, UseDefaultName, name)`
- Builder 工厂在 `CAMOperationCollection`（工序类）与 `NCGroupCollection`（组/刀具/几何类）上，不在 CAMSetup 上
- `CavityMillingBuilder.DepthPerCut`、`MillCutParameters.FloorStock/Stepover`、`HoleMachiningCutParameters.BottomStock/BottomClearance`、`FeedsBuilder.SpindleRpmBuilder/FeedCutBuilder`
- 刀路统计：`Operation.GetToolpathTime/GetToolpathLength/GetToolpathCuttingTime/GetToolpathCuttingLength`
- 批处理下 `PartCollection.Open` 不建显示（组创建报 "No active work part"），用 `OpenDisplay` + `SetWork`

## M1：导出侧适配器（M1_Export.vb，✅ 批处理达标 2026-09-02）

| # | 核对点 | 结果 |
| :--- | :--- | :--- |
| 1 | plan.json 通过 schema 校验 | ✅ 0 错误（公制模板；schema 曾缺 NX 真实枚举值 DEPTH_FIRST/UP，已补合同） |
| 2 | 无 ERROR 级诊断；warning 全部可归因 | ✅ 无 ERROR；warning 三类可归因：非标准刀具组（模板树常态）/ builder 类型未入表（other 兜底）/ 参数按 builder 类型不可得（UNRESOLVED，预期缺项） |
| 3 | 两次导出字节级一致（确定性） | ✅ 字节级一致 |
| 4 | 双射覆盖 | ✅ 15 工序 / 4 刀具 / 1 setup / 19 workplan 元素 |
| 5 | 会话只读 | 🔧 待 GUI 核对（批处理内无 Commit 调用，结构性保证） |
| 6 | 生效值直读 | 🔧 待 GUI 抽查 ≥3 项（M1_AttrProbe 已证 DepthPerCut/FloorStock/WallStock 直读成功） |

## M2：执行侧适配器（M2_Rebuild.exe，✅ GUI 会话实测达标 2026-09-03）

> **运行方式（实测修正 2026-09-03）**：`.vb` 无法经 File → Execute → NX Open 执行（只收
> dll/exe/jar/class）；run_journal 不设 UGII_BATCH_MODE 也只是带 UI 的 journal 会话——
> 不初始化加工模板注册表（组/工序 subtype 缺失，Create 必失败；M0_Templates 交互复测 +
> 打开 CAM 零件预热均无效，见 M0 结论 6）。可行路径：**真 GUI 会话**（手动打开 CAM 零件进入
> 加工环境）→ File → Execute → NX Open 执行**编译入口 EXE**（VB 装载器用 vbc 编 exe，
> 装载逻辑不变：Assembly.LoadFrom adapter → 反射调 JournalEntry.M2Rebuild）。
> 已编译：`C:\nx-vibe-journal-out\M2_Rebuild.exe`（重编译命令见 M2_Rebuild.vb 头注）。

| # | 核对点 | 结果（2026-09-03） |
| :--- | :--- | :--- |
| 1 | 命令序列全量执行无异常 | ✅ 28/28 全完成，无 ERROR 诊断（首跑逐步修复链见下） |
| 2 | 零遗留 Builder/对话框 | ✅ 执行后无残留对话框（目视） |
| 3 | prj′ 可打开、可回读 | ✅ M3Loop 内 SaveAs `parts\rebuild_part.prt`；再导出计数与 plan 一致（15 工序/1 setup/19 workplan） |
| 4 | 缺字段不产生 Set | ✅ technology 98/98、strategy 61 中 53 匹配，偏差 8 条全归因（见下）；枚举宽松匹配后零 PARAM_SET_FAILED |
| 5 | 工序无几何关联（D-适配-2 预期） | ✅ 工序挂几何组 SETUP-005（= plan MCS 组，正确）；面/边界选择区为空 |
| 6 | MCS 反射设置（SetMcsReflective）落点正确 | ✅ 报告 mcs 维度 compared=1 matched=1 偏差 0（origin/轴与 plan 0.01mm 内一致） |
| 7 | 工具视图实物 | ✅ NONE + T-001..T-004 四把刀；无 T-005（导出侧幻影，见 M3 归因） |

> **调试历程要点**（2026-09-03，供复现）：① Create* 键语义 = (setup 族, subtype)，旧式
> typeName 全失效（探针 M2_Probe2 ③ 实证：`CreateMethod(parent,"mill_planar","MILL_METHOD",…)`
> ✓）→ executor 键表化 NxTemplateKeys（plan 导出类型 → mill_planar 族 Operation subtype 反向表
> 亦由探针 ③ 实测：FACE_MILL_ZIGZAG→FacingZigZagBuilder 等）；② GUI 会话新 setup 自带模板默认组
> （ProgramOrder=[NONE,PROGRAM]、MachineMethod 含 MILL_ROUGH 等、Geometry=[NONE,MCS_MAIN]）→
> 组命令 find-or-create 复用同名组（复用更忠实：与原件同源于模板派生）；③ plan workplan 根节点名 /
> 方法组约定名 == 视图根组自身名（NC_PROGRAM / METHOD）→ 根组即目标组；④ plan 大写蛇形枚举 ↔ NX
> Pascal 枚举（LEVEL_FIRST↔LevelFirst）→ SetLeaf 宽松等价匹配，失败降级 warning + 跳参（失败隔离）。

## M3：完整闭环（M3_Partial.vb 批处理部分闭环 ✅ / M3_Loop.exe 完整闭环 ✅ 2026-09-03 全归零）

| # | 核对点 | 结果 |
| :--- | :--- | :--- |
| 1 | 报告生成且过报告 schema 校验 | ✅ 完整闭环报告 0 校验错；批处理部分闭环 0 错 |
| 2 | 偏差类别符合预期且每条可归因 | ✅ **最终态（2026-09-03 收敛）：真实偏差（WARNING 级）= 0**。deviations 数组仅 7 条 known_skip（INFO 豁免记录，结构化命中 NxWriteProtection 表）。structure=1.0（15/15 配对）；tool 维度 65/65 匹配 0 偏差；strategy 55 匹配 + 7 豁免；parameter 全匹配；mcs 全匹配；geometry 0/0 = 几何不读（D-适配-2 镜像）已知限制 |
| 3 | 报告确定性 | ✅ 导出字节级一致 + Core 确定性锁定 + 三轮复跑同结果 |
| 4 | 完整闭环（GUI）：导出 → 重建 → 再导出 → 跨件对比 | ✅ m3_loop.txt：15 工序/4 刀具/1 setup ×2 导出 schema 0 错 → 28 命令全执行 → prj′ 落盘 → plan″ 15 工序/4 刀具（同构）→ Compare → 报告 0 校验错 |

> **闭环判定（2026-09-03 终态）**：structure=1.0 + tool 65/65 + parameter 全匹配 =
> 「plan 合同可无歧义重建」在真实 NX 完全成立；7 条 known_skip 全部为 NX 写保护豁免
> （Facing/EdgeChamfer 的 stock 类字段 Commit 必回滚，E 段一段式/两段式对照实证——重建值由
> NX 模板固化，plan 无法驱动，结构化豁免不静默）。
> **收敛历程**：19（tool 物化 + 写路径缺口）→ 23（CanWrite 回归，已归因）→ 18（CanWrite 修复）
> → 12（组名复用 + 写保护跳过）→ 7（豁免口径统一 + 两侧都有值豁免）→ **0 真实偏差**
> （FormMill 读取补全 + MILL 类型落地）。全程无 Core 缺陷，全部为适配层保真度缺口。

## 通用注意事项

- 批处理会话初始无 Work Part——journal 内需 `Parts.Open`/新建 part；
- 许可缺失的表现是操作创建抛异常（非静默）——journal 捕获并如实落输出；
- 每次运行后检查 `C:\nx-vibe-journal-out\` 下的输出文件，不要依赖控制台；
- 批处理运行中勿同时操作宿主机上的 NX GUI 会话（许可/文件锁冲突）。
- 编译 adapter 前须退出全部 NX 会话（EXE 进程锁住 bin 目录 DLL，MSB3021）；同一 GUI 会话内复跑
  M2/M3 前先关闭旧 `rebuild_part` 零件（NewDisplay 同名/残留组冲突）。
