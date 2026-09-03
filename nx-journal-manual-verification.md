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

## M4：几何闭环（子步0 探针 v1-v4 实证 2026-09-03，GUI 运行待执行）

> M4 = 解除 D-适配-2/3 镜像（几何维度三方从空转 0/0 到实读）+ STEP 跨件（nx-plugin-design §7-5）。
> 探针链：`M4_GeoProbe.vb`（vbc → exe）→ `JournalEntry.M4GeoProbe` → `NxGeometryProbe`
> （Export/NxGeometryProbe.cs）。输出 `C:\nx-vibe-journal-out\m4_geoprobe.txt`。

**v1-v3 批处理实证结论（m1_template_metric.prt）**：
- 读链离线定位成立：builder 几何角色 → `.GeometryList → GetContents → GeometrySet.GetItems`；
  另有 GeometryCiBuilder 通用角色（全部 Builder 都有）与 Boundary/BoundaryPlanarMill 角色（平面族）。
- **批处理加载态下所有几何容器为空**：CAM.Geometry 五角色（Blank/Wall/Check/CutArea/Part）各
  set=1 但 items=0（`InitializeData(true)` 重载后仍 0）；BoundarySetList→list[1] 但
  BoundaryMemberSetList→list[0]；组级仅 Orient 型（NONE/MCS/MCS_MAIN，无 WORKPIECE）。
- 模板件工序为自动/特征驱动（AutoWallSelection=true；op 带 InsertFeature/RemoveFeature），
  且几何物化呈会话耦合（与 M0 模板注册表同源的三态差异）——**几何读取须 GUI 显示态验证**。
- 确定性 ✓（两遍指纹一致）；H15005 批处理 OpenDisplay/Parts.Open 均返 null（文件级加载失败，GUI 待验）。
- P3 离线已证：.NET UF **无** ask_face_area/ask_face_normals（nxopen-research §2.3 过时），
  只有 AskFaceData（类型码/面内点/方向/盒）+ AskFaceProps（(u,v) 处单位法向）；托管无 Measure* 创建入口。

| # | 探针问题 | GUI 判读指引 |
| :--- | :--- | :--- |
| 1 | P1 各 op 的 Builder 上哪些几何角色承载关联几何 | `role: sets=N items=M`（角色名频次表 = M4a 映射表输入）；期望显示态下面铣/平面类 op 至少一角色有 items |
| 2 | P2 载荷形态（Face/Edge/BoundaryMember/其他） | 元素类型名决定 M4a 分类口径；若 GUI 下仍全空 → 模板件工序确无显式存储几何，M4 需显式面选择 ground truth（用户 GUI 手编小件） |
| 3 | P3 面属性 API | AskFaceData/AskFaceProps 在真实面上是否可读；面积/质心路线（两成员无命中 → 边界环积分或测量 API 次轮探针） |
| 4 | P4 两遍枚举指纹一致 | 确定性硬验收（导出侧字节级一致的前提） |

> 运行方式（GUI 会话，同 M2/M3）：退出全部 NX → `dotnet build` 适配层 → vbc 编译
> `M4_GeoProbe.vb` → `C:\nx-vibe-journal-out\M4_GeoProbe.exe`（编译命令见该文件头注 / compile_m4_probe.bat）→
> NX GUI 打开加工环境 → File → Execute → NX Open 选 M4_GeoProbe.exe →
> 回传 `m4_geoprobe.txt` 判读（loader 会自行打开/定位 m1_template_metric.prt + H15005-307(1).prt）。

> **2026-09-03 GUI 实测补充（写探针 v1-v5 全链实证）**：几何容器在 GUI 与批处理一致全空；
> H15005 GUI 亦不可用。程序化自建 ground truth（`M4_WProbe.vb` → `JournalEntry.M4GeoWriteProbe`
> → `NxGeoWriterProbe`）逐轮实证：方块体/刀具组/工序（POCKETING 系，VolumeBased25D 五角色）
> 创建全部可行（Create 传 subtype=null 会原生 AV——键表 key 是 plan 类型非模板名）；
> 但几何写/读全部落空：`AppendGeometrySet` 原生 AV（模板集语义不明）；`CreateGeometrySet +
> Selection.Add(单参)` 调用成功 + InitializeData(true) + Commit 后回读仍 sets=1 items=0
> （工序级五角色 + 组级 WORKPIECE 的 PartGeometry/CheckGeometry 全部如此——Add 不向
> CAM 数据库物化）；几何树实证结构 `GEOMETRY→MCS_MAIN→WORKPIECE→MCS_LOCAL`；
> MillGeomBuilder 属性面揭示 BlankGeometry:GeometryGroup / GeometryCiBuilder / LayoutCiBuilder
> 等内部模型。**收敛结论（M4 几何维度定案）**：NX2406 的 2.5D 域（mill_planar 族）工序/组几何
> 为 CAM 内部模型（特征/Ci/ScCollector/拓扑驱动），裸 Tag 面/体不向 `CAM.Geometry.GetItems`
> 物化——工序级与组级、模板态与挂接态、批处理与 GUI 全一致。plan 的工序级
> `geometry_ref`（face anchor）合同在 2.5D NX 侧无落点 → 该维度按 **known-skip（NX API
> 边界，INFO 结构化豁免）** 收口，工序级面合同验证归 3D 域（mill_contour 等显式面选择域 +
> FaceResolver）后置（与 nx-plugin-design §6 2.5D 边界声明一致）。Core 侧 faces→features
> anchor 管线已由合成快照实证（features 含 anchor=3、schema 0 错）——合同与拍平逻辑无缺陷。
> M4 余项：M4c STEP 跨件闭环（结构/刀具/参数/策略/MCS 维度，与工序几何无关）→ **✅ 2026-09-03 完成**
> （见下 M4c 节：S0 探针定案 + S2 主闭环归零复现，D-适配-3 解除）。

## M4c：STEP 跨件闭环（S0 双态实证 ✅ + S2 主闭环归零复现 ✅ 2026-09-03）

> M4c = STEP 打开（解除 D-适配-3 的副本口径）+ 跨件闭环（结构/刀具/参数/策略/MCS 维度，
> 与工序几何无关——几何 known-skip 定案见上 M4 节）。探针链：`M4c_StepProbe.vb`
> （vbc → exe / run_journal 直跑 .vb 均可——Q4 判读即两态对照）→ `JournalEntry.M4cStepProbe`
> → `NxStepOpenProbe`（Export/NxStepOpenProbe.cs）。输出 `C:\nx-vibe-journal-out\m4c_stepopen.txt`。

**静态实证（m4c_reflect1-4.ps1，无 NX 会话即答，产物留档 C:\nx-vibe-journal-out\m4c_reflect*_out.txt）**：
- Session 无 Step/Import/Export 成员；`StepImportBuilder` / `StepExportBuilder` /
  `Features.ImportFeatureBuilder` 不存在；PartCollection 仅 Open*/OpenBase 系（无 STEP 专用直开入口）；
- **STEP 翻译器 = `Session.DexManager.CreateStep203/214/242Importer()`**（Builder 语义：
  InputFile / ImportTo[WorkPart|NewPart] / FileOpenFlag / SewSurfaces / Optimize… + Commit/Destroy）——
  Q1 的代码面已答，运行时只验 Commit 成败与产物形态（批处理/GUI 差异）。

| # | 探针问题 | S0 双态实证结论（2026-09-03，批处理 run_journal + GUI File→Execute 各一轮） |
| :--- | :--- | :--- |
| 1 | Q1 STEP 打开路径 | **OpenDisplay 直开成功（双态同构）**：隐式翻译生效 → leaf=m4_gt_face_stp、bodies=1 → **M4c 打开路径定案 = 直开，无翻译器仪式**。DexManager 203/214/242（NewPart，SetMode=NativeFileSystem 后）Commit 返 null 静默失败——双态复现、与显示态无关 → known-issue 收口（后续需要 schema 强控/merge 导入时另开专项） |
| 2 | Q2 产物挂 CAMSetup | **路径A ok（双态）**：直开产物挂 mill_planar 成功 → **M4c 重建载体定案 = OpenDisplay 直开 + CreateCamSetup**（D-适配-3 副本口径解除路径）。注意：无 CAM 零件读 `.CAMSetup` 属性即抛（非返 null），判读时勿误读。路径B（WorkPart 导入已挂 CAM 宿主件）报 "Part Import Error: Unable to import selected file to work part" → 弃 |
| 3 | Q3 落位 | STEP 翻译件 WCS [0,0,0] = 对照件原点、bodies=1 几何完整 → **无坐标漂移**（MCS 对比基准风险排除） |
| 4 | Q4 模式差异 | **双态同构**：Q1-①/Q2 两态结果一致；唯一差异 = GUI 错误消息本地化中文（判读无碍）→ M4c 后续验证两态皆可跑，批处理优先 |

> 运行方式：退出全部 NX → `dotnet build` 适配层 → vbc 编译（compile_m4c_step_probe.bat）→
> 批处理：`UGII_BATCH_MODE=1 "…\NXBIN\run_journal.exe" M4c_StepProbe.vb`；
> GUI：NX 进入加工环境 → File → Execute → NX Open 选 M4c_StepProbe.exe → 回传 `m4c_stepopen.txt` 判读。

> **S1（fixture，已完成 2026-09-03）**：STEP 主题件 = `parts\m4_gt_face.stp`（AP214 实落盘，8KB，
> ST-Developer/NX2406.1700）。fixture 缺失时探针自动从对照件导出（`CreateStepCreator` 批处理可用，
> **输出文件名 = 零件名**、OutputFile 仅定目录）；自动导出失败才需 GUI 手动兜底（导出名即零件名）。
> 旁证：空体件（m1_template_metric.prt bodies=0）自动导出**静默无产物** → 空体 STEP 路线出局；
> H15005-307(1).prt 批处理不可开（No Displayed Part，M4 先例一致）→ GUI-only 件，未选用。

> **S2（主闭环，✅ 2026-09-03 GUI 实测归零复现）**：`M4c_Loop.vb` → `JournalEntry.M4cLoop`——与 M3Loop
> 同构，**唯一差异 = 重建载体从另存副本换成 STEP 直开翻译件**（源 m1_template_metric.prt 现导 ground
> truth plan → 载体 parts\m4_gt_face.stp OpenDisplay 直开 + CreateCamSetup → 重建 28/28 → 现导 plan″
> → Compare）。**实测报告与 M3 副本口径逐字段一致**：structure 15/15（0 类型错/0 乱序/0 组差）、
> tool 65/65、parameter 98/98、strategy 55 匹配 + 7 known_skip、mcs 1/1、geometry 0 对比（known-skip
> 口径）、deviations 7 = 与 M3 同款豁免（mill_face/mill_chamfer 的 floor_stock/part_stock/depth_per_cut
> 写保护表豁免，plan-comparer §3.8）→ **载体依赖偏差 = 0，plan 合同跨文件载体无歧义重建实证，
> D-适配-3 解除**。归档：`C:\nx-vibe-journal-out\m4c_vs_m3.txt`（双报告逐字段对照）。
> ground truth 选型依据：m1 件工序类型全在重建键表（M3 归零实证）；m4_gt_face.stp 载体几何/落位
> S0 已证；m4_gt_face.prt 与空体件/H15005 件不可用原因见 S1 段旁证。
> 运行方式：GUI 会话（op 模板注册表仅 GUI 加载）→ File → Execute → NX Open 选 M4c_Loop.exe；
> 批处理不可跑（Create 必失败），无需尝试。

## 通用注意事项

- 批处理会话初始无 Work Part——journal 内需 `Parts.Open`/新建 part；
- 许可缺失的表现是操作创建抛异常（非静默）——journal 捕获并如实落输出；
- 每次运行后检查 `C:\nx-vibe-journal-out\` 下的输出文件，不要依赖控制台；
- 批处理运行中勿同时操作宿主机上的 NX GUI 会话（许可/文件锁冲突）。
- 编译 adapter 前须退出全部 NX 会话（EXE 进程锁住 bin 目录 DLL，MSB3021）；同一 GUI 会话内复跑
  M2/M3 前先关闭旧 `rebuild_part` 零件（NewDisplay 同名/残留组冲突）。
