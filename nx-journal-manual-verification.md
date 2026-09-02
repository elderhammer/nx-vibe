# NX Journal 验证清单（M0-M3 手动核对）

> 更新时间：2026-09-02
> 用途：适配层验证的核对清单（nx-adapter.md §6）。批处理验证（run_journal）与
> 宿主机 GUI 目视核对共用。被 nx-plugin-design.md §4 引用（此前缺失，本次补建）。
> 运行方式：`UGII_BATCH_MODE=1 "…\NXBIN\run_journal.exe" <journal.vb>`（SSH/批处理）；
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
| 6 | **对象模板注册表（新发现）** | **批处理下不加载**：setup 模板注册 ✓（17 个），但组/工序 `Create` 的 typeName 注册表（"PROGRAM"/"CAVITY_MILL"…）由 NX UI 网关初始化，batch 模式跳过——`SpecifyConfiguration`/`AddTemplateType`/`ApplicationSwitchImmediate(UG_APP_MANUFACTURING)` 均无法补救 |

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

## M2：执行侧适配器（M2_Rebuild.vb，🔧 代码编译级验证；执行验证在 GUI 会话）

> 批处理限制：对象模板注册表不加载（M0 实测），组/工序 Create 必失败——journal 如实输出。
> **GUI 运行方式**（宿主机 NX 交互会话）：NX 菜单 File → Execute → NX Open 选本 vb 文件，
> 或命令行 `"…\NXBIN\run_journal.exe" M2_Rebuild.vb`（不设 UGII_BATCH_MODE）。

| # | 核对点 | 通过判据 |
| :--- | :--- | :--- |
| 1 | 命令序列全量执行无异常 | journal 输出「命令执行: 全部完成（N 条）」 |
| 2 | 零遗留 Builder | 执行后无未 Commit 状态（GUI 无残留对话框） |
| 3 | prj′ 可打开、可回读 | 重建副本重新导出，工序/组计数与 plan 一致 |
| 4 | 缺字段不产生 Set | 抽查：plan 未给出的参数字段在重建工序上呈继承态（GUI 对话框灰显/继承标记） |
| 5 | 工序无几何关联（D-适配-2 预期） | 重建工序几何选择器为空 |
| 6 | MCS 反射设置（SetMcsReflective）落点正确 | GUI 打开重建 MCS 对话框核对 origin/轴与 plan 一致 |

## M3：最小闭环（M3_Partial.vb ✅ 批处理达标 / 完整闭环在 GUI）

| # | 核对点 | 结果 |
| :--- | :--- | :--- |
| 1 | 报告生成且过报告 schema 校验 | ✅ 批处理部分闭环 0 错误 |
| 2 | 偏差类别符合预期且每条可归因 | ✅ 自对比零偏差；geometry 评分 0 = 几何不读（D-适配-2 镜像）的已知限制显形 |
| 3 | 报告确定性 | ✅ 导出确定性已证；报告确定性由 Core 锁定 |
| 4 | 完整闭环（GUI）：导出 → 重建 → 再导出 → 跨件对比 | 🔧 待 GUI（M2 打通后，把重建副本重新导出的 plan″ 与 plan 送入 PlanComparePipeline） |

## 通用注意事项

- 批处理会话初始无 Work Part——journal 内需 `Parts.Open`/新建 part；
- 许可缺失的表现是操作创建抛异常（非静默）——journal 捕获并如实落输出；
- 每次运行后检查 `C:\nx-vibe-journal-out\` 下的输出文件，不要依赖控制台；
- 批处理运行中勿同时操作宿主机上的 NX GUI 会话（许可/文件锁冲突）。
