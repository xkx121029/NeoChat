# ADR-001 — 技术栈：双原生 + 无 WebView 主路径 + 渲染等价性保障

- 状态：**Accepted**（2026-11-22 人类裁决，见 [RULINGS-2026-11-22.md](RULINGS-2026-11-22.md)）
- 日期：2026-11-22
- 决策者：Tech Lead
- 影响范围：全部泳道

## 1. 背景

产品定义要求"流式与富文本渲染达到同级最优"，且明确禁止 Electron、禁止 WebView 承载主界面、要求原生控件与系统集成（托盘、全局热键、无障碍）。同时双端必须行为等价。

## 2. 候选方案与取舍

### 2.1 为何不用 Electron

| 维度 | Electron（参照量级） | 双原生 |
| --- | --- | --- |
| 空闲内存基线 | 200~350MB（Chromium 多进程，单窗口） | Windows WinUI 3 空壳 40~70MB；Android Compose 空壳 60~90MB |
| 冷启动到可交互 | 1.2~2.5s（需预加载技巧） | 目标 0.8~1.2s（Windows，待实测），0.6~1.0s（Android，Baseline Profile） |
| 系统集成 | 托盘/热键需 native addon，全局热键与 UIPI 交互易出问题；无障碍需自建桥 | 原生 API 直连 |
| 包体 | ≥ 80MB 起 | Android ≤25MB / Windows ≤60MB 目标 |
| 渲染一致性 | 天然一致 | **需要自建等价性保障（本 ADR 第 5 节）** |

关键判据是产品定义那句"渲染引擎必须是原生控件"。Electron 的内存与包体数字本身已越过地板表，但它真正的否决理由是：Windows 上托盘 + 全局热键 + Narrator 无障碍、Android 上 edge-to-edge + 系统 TTS + 分享接收，这四项用 Chromium 壳做出来都是"能跑但会在细节上露馅"，且每一项都要维护一层 native 桥。**结论：否决。**

### 2.2 为何不用 Tauri / WebView 作为主路径

WebView 方案在 Mermaid 与 LaTeX 上确实成熟：KaTeX 与 mermaid.js 是行业事实标准，自研替代品达到 90% 兼容性需要 3~5 个月。但代价是：

1. 性能地板被 WebView 接管。1000 条消息滚动、10 万字符单条消息、逐 token 帧抖动 P99 ≤8ms，这三项无法在 WebView 内保证——我们只能测到"WebView 表现如何"，无法"优化到哪个数"。
2. 无障碍断裂：TalkBack/Narrator 在 WebView 内看到的是 DOM 无障碍树，与原生语义（列表、按钮、进度）不一致，流式播报节流也无法统一。
3. i18n 断裂：WebView 内容区不跟随系统语言与字体缩放（200% 字体放大要求）。
4. 内存：每个消息区一个 WebView 实例的方案不可行，单 WebView 全局长列表又会把虚拟化交给 JS。

**结论：主路径禁用 WebView。** 但保留一个有边界的例外：**Mermaid 只做原生布局子集的降级方案**（见 2.4）。

### 2.3 为何不用单一 KMP UI（Compose Multiplatform Desktop）

| 维度 | CMP Desktop | WinUI 3 |
| --- | --- | --- |
| 启动 | JVM + Skia 初始化，冷启动预估 1.5~2.5s，需自建 AOT/裁剪方案 | 原生 XAML 预热，1.0~1.5s（待实测） |
| 空闲内存 | JVM 堆基线 120~200MB | 40~70MB |
| 系统集成 | 托盘/热键/跳转列表/单实例需 JNI 桥，无官方支持 | 原生 |
| 无障碍 | Skia 自绘，需自建语义树 | XAML 自动语义 |
| UI 代码共享 | 60~75%（估算） | 0% |

省下的 UI 代码量（约 6~8 人月）被三处吃掉：JVM 启动与包体优化、Windows 系统集成的 JNI 桥、自建无障碍语义树。**结论：否决，双端各写 UI。** 共享部分放在协议层、领域逻辑、RenderModel 与 spec，这是收益最高、耦合最低的一层。

### 2.4 Mermaid 的原生方案与降级边界

- v1 必做：`flowchart TD/LR`（含 subgraph、菱形/圆角/矩形节点、实线/虚线/带标签边）与 `sequenceDiagram`（参与者、消息、注释、alt/opt 块）。
- **人类裁决（2026-11-22）：只做 flowchart 与 sequenceDiagram，静态渲染、无动画，禁用“尽力支持”口径。** 每个图要么正确渲染，要么降级为代码块，不存在中间态——消灭“看起来支持了但布局偶尔错”这种最难验收、最耗支持成本的形态。
- v1 不做：class、state、gantt、pie、mindmap、git graph。这些渲染为**代码块 + "复制源码" + "在浏览器查看"链接**（链接由用户点击外部打开，不在应用内加载）。
- 布局算法：分层布局（Sugiyama 简化版：分层 → 层内排序用重心法 → 正交走线 → 边交叉最小化），在后台 dispatcher 计算，UI 只接收节点坐标与边路径。
- 100 节点流程图 ≤200ms 的判据：**布局计算 ≤200ms 且在后台线程**，首帧呈现不阻塞 UI 线程。不承诺"端到端 200ms 内像素上屏"——那是另一条指标（首帧 ≤400ms）。
- 失败兜底：解析失败、超过 200 节点、超过 2000 条边 → 一律降级代码块并给出 `mermaid_too_complex` 提示，绝不白屏或抛异常。

### 2.5 WinUI 3 + NativeAOT 兼容性评估结论

- **NativeAOT：不可行。** Windows App SDK 的 XAML 类型激活依赖运行时反射与 CsWinRT 投影，XAML 编译器生成的 `IXamlMetadataProvider` 在 AOT 下无法保证类型可达；WinAppSDK 官方未声明 AOT 支持。
- **PublishTrimmed：默认关闭。** XAML 反射面使裁剪不安全；仅在可证明无反射的子程序集上按程序集粒度裁剪（M1 时评估，需附裁剪后回归测试）。
- **采用的发布形态：`PublishReadyToRun` + 单文件（`PublishSingleFile`）+ 关闭裁剪。** 代价：包体偏大（预估 55~75MB，含 WinAppSDK 运行时自包含），冷启动受益于 R2R。
- **复审触发条件**：任一条成立即重评——(a) WinAppSDK 文档声明支持 AOT/裁剪；(b) .NET 主版本升级；(c) 冷启动实测连续两轮不达地板。
- **人类裁决（2026-11-22）：启动拆三档，NativeAOT 只作加分项。** 单一“冷启动到可交互 0.8s”把 XAML 加载、WinAppSDK 激活、数据库打开、会话列表首屏压成一个数，不可归因，故拆为三档，各自配地板线：

| 档位 | 卓越线 | 地板 | 测量入口 |
| --- | --- | --- | --- |
| 首帧可见（进程不驻留、无系统缓存预热） | ≤0.9s | ≤1.2s | `bench/windows/startup:first-frame` |
| 首屏可交互（会话列表可滚动可点击） | ≤1.2s | ≤1.6s | `bench/windows/startup:interactive` |
| 托盘唤起（warm，进程已驻留） | ≤120ms | ≤200ms | `bench/windows/startup:tray-wake` |

  冷启动类指标前 3 次单独报告（真冷），随后 17 次另报（系统已缓存）。NativeAOT 若将来可行，视为对上述数字的加分项，**不是验收前提**。

## 3. 决策

1. Android：Kotlin 2.x + Compose + Material 3，minSdk 26，架构 MVVM + 单向数据流，OkHttp + 自研 SSE 解析，Room + FTS5，DataStore，DI 待 M1 在 Hilt/Koin 间择一并补 ADR。
2. Windows：C# 13 / .NET 9 + WinUI 3 + CommunityToolkit.Mvvm，Markdig 解析后自绘渲染（不自建富文本控件框架，用 `RichTextBlock` + 自定义块渲染器），ColorCode 高亮，CSharpMath 渲染 LaTeX，ItemsRepeater 虚拟化，Microsoft.Data.Sqlite + FTS5。
3. 共享层不是代码而是 **spec + conformance 样本 + RenderModel 金样本**：双端各自实现，用同一批字节与同一批模型做机器比对。
4. 发布：Windows 用 MSIX（主）与单文件（备），Android 用 AAB + APK 双产物。

## 4. 后果

**正面**：系统集成与无障碍走原生路径，成本最低；性能指标可优化可归因。

**负面（必须登记）**：
- 双端 UI 与渲染器各写一遍，UI 层工作量约翻倍（预估 +4~6 人月）。
- 双端渲染等价性靠工程保障而非架构保障，需要 20 组基准用例 + 每 PR 截图比对，长期维护成本存在。
- Mermaid 覆盖率低于 WebView 方案，用户可见功能缺口必须产品明示。
- 无 NativeAOT，Windows 包体与启动时间存在硬下限。

## 5. 渲染等价性保障方案（配套决策）

“视觉等价”不可能字面成立（字体不同、行高不同、抗锯齿不同）。人类裁决（2026-11-22）确认定义为**四层等价**，每层都由机器判定，不靠人眼：

| 层 | 判据 | 判定方式 | 容差 |
| --- | --- | --- | --- |
| L1 模型等价 | 同一输入 Markdown → 同一 `RenderModel` JSON | 金样本逐字段 diff | **0 差异** |
| L2 结构等价 | 同一 `RenderModel` → 双端渲染出相同的块序列与行数 | 双端导出“渲染清单”（块类型 + 行数 + 文本内容哈希） | **0 差异** |
| L3 视觉等价 | 截图比对 | 块边界与文本基线位置偏差 | **Android ≤2dp / Windows ≤2px**；文本内容逐字符一致；字体度量、抗锯齿、亚像素差异不计 |
| L4 行为等价 | 交互脚本回归（同一脚本双端各跑一次） | 12 条脚本：代码块复制、长按菜单、折叠/展开、链接点击、表格横向滚动、图片查看、引用跳转、分支切换、搜索命中跳转、任务列表勾选（只读语义）、脚注跳转、Mermaid 降级复制 | 全部通过 |

L4 不替代 L3：截图负责捕获纯视觉回归，脚本负责捕获“看起来对但点不动”的缺陷。

20 组基准用例（含长代码块、嵌套列表、复杂表格、行内公式、Mermaid、中英混排、RTL、emoji、脚注、任务列表）由 M0-CON-004 定义，金样本由 M3 产出并存 `bench/render-goldens/`。

## 6. 复审触发条件

- 冷启动连续两轮不达地板
- WinAppSDK 对 AOT/裁剪的支持状态变化
- Mermaid 用户请求覆盖率 >5% 集中在未支持的图类型
- 双端 L1 等价性连续两次因 spec 歧义失败
