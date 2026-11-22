# NeoChat 设计系统规格（自 dsh-Go 提取）

| 项 | 值 |
| --- | --- |
| 参考仓库 | `ccch1mneyyy/dsh-Go`（公开） |
| 读取分支 | `main` |
| 树快照 commit | `3c8637d2b1279d8b420e148bba4624cb355eb182` |
| 读取方式 | `api.github.com/repos/.../git/trees/main?recursive=1` + `raw.githubusercontent.com/.../main/<path>`（本机 `github.com:443` 不通，未克隆） |
| 源码路径前缀 | 仓库根有 `app/` 一级目录，故真实路径形如 `app/lib/ui/theme/palette.dart` |
| 参考技术栈 | Flutter 3.x / Dart，Material 3 手工配色（不走 `ColorScheme.fromSeed`） |
| 目标技术栈 | Android：Kotlin + Jetpack Compose + Material 3；Windows：C# + WinUI 3 |
| 单位约定 | Flutter logical pixel ≡ Compose `dp` ≡ WinUI 3 `epx`（96 DPI 基准 1:1）。本文所有尺寸若未注明，均按此 1:1 直搬。 |
| 标注约定 | 每条数值后紧跟 `〔路径:行号〕`。凡为推导/换算/源码未给出者，行内标 **推断** 并写明依据。 |

本文件只写规格，不写实现代码。凡源码里没有的数值，写"未在源码找到"，不补近似值。

---

## 1 设计令牌

### 1.1 颜色 · 亮色（晨雾，默认配色）

基线：`DshPalette` 为手工构造的 ColorScheme，**不用** `ColorScheme.fromSeed`（理由见 `palette.dart:11-14`：M3 tonalSpot 会把低饱和意图抹掉）。`surfaceTint` 被显式设为透明，层次不靠给面染主色 〔`app/lib/ui/theme/palette.dart`:160-161〕。

| ID | 令牌 | 值 | 出处 |
| --- | --- | --- | --- |
| L01 | `mistTop` 页面渐变顶 | `#EBF1FE` | `app/lib/ui/theme/palette.dart`:37 |
| L02 | `mistBottom` 页面渐变底 | `#F6F7FC` | `app/lib/ui/theme/palette.dart`:40 |
| L03 | `mist` 不透明页面底（渐变中值兜底） | `#F0F4FD` | `app/lib/ui/theme/palette.dart`:43 |
| L04 | `mistLowest` | `#FBFCFF` | `app/lib/ui/theme/palette.dart`:46 |
| L05 | `mistLow` | `#F6F9FE` | `app/lib/ui/theme/palette.dart`:47 |
| L06 | `mistContainer` | `#EEF1F9` | `app/lib/ui/theme/palette.dart`:48 |
| L07 | `mistHigh` | `#E7EBF4` | `app/lib/ui/theme/palette.dart`:49 |
| L08 | `mistHighest` | `#DFE4EE` | `app/lib/ui/theme/palette.dart`:50 |
| L09 | `ink` 主文字 | `#1E232E` | `app/lib/ui/theme/palette.dart`:53 |
| L10 | `inkMuted` 次级文字 | `#4F5562` | `app/lib/ui/theme/palette.dart`:56 |
| L11 | `inkFaint` 三级文字（时间戳/占位符） | `#646974` | `app/lib/ui/theme/palette.dart`:59 |
| L12 | `inkGhost` 禁用/装饰 | `#A1A5AC` | `app/lib/ui/theme/palette.dart`:62 |
| L13 | `outlineLight` 交互边界 | `#7E838D` | `app/lib/ui/theme/palette.dart`:65 |
| L14 | `ruleLight` 分割线/发丝描边 | `#DCE0E8` | `app/lib/ui/theme/palette.dart`:68 |
| L15 | `shadowInk` 阴影基色（带蓝） | `#0B152D` | `app/lib/ui/theme/palette.dart`:71 |

### 1.2 颜色 · 暗色（深海石板）

页面明度 0.23–0.25，**不是**纯黑底（`palette.dart:19-22`）；大面积面色不出现纯白/纯黑 〔`app/lib/ui/theme/palette.dart`:22〕。

| ID | 令牌 | 值 | 出处 |
| --- | --- | --- | --- |
| D01 | `nightTop` | `#1D222C` | `app/lib/ui/theme/palette.dart`:74 |
| D02 | `nightBottom` | `#1B1C23` | `app/lib/ui/theme/palette.dart`:75 |
| D03 | `night` | `#1C1F28` | `app/lib/ui/theme/palette.dart`:76 |
| D04 | `nightLowest` | `#14171E` | `app/lib/ui/theme/palette.dart`:77 |
| D05 | `nightLow` | `#232730` | `app/lib/ui/theme/palette.dart`:78 |
| D06 | `nightContainer` | `#272C36` | `app/lib/ui/theme/palette.dart`:79 |
| D07 | `nightHigh` | `#2D323C` | `app/lib/ui/theme/palette.dart`:80 |
| D08 | `nightHighest` | `#343944` | `app/lib/ui/theme/palette.dart`:81 |
| D09 | `paper` 主文字 | `#E6E9F0` | `app/lib/ui/theme/palette.dart`:84 |
| D10 | `paperMuted` | `#B6BBC5` | `app/lib/ui/theme/palette.dart`:85 |
| D11 | `paperFaint` | `#9A9EA9` | `app/lib/ui/theme/palette.dart`:86 |
| D12 | `paperGhost` | `#656971` | `app/lib/ui/theme/palette.dart`:87 |
| D13 | `outlineDark` | `#737781` | `app/lib/ui/theme/palette.dart`:88 |
| D14 | `ruleDark` | `#343841` | `app/lib/ui/theme/palette.dart`:89 |
| D15 | `shadowNight` | `#03060D` | `app/lib/ui/theme/palette.dart`:90 |

### 1.3 中性辅色 / 语义色（两套共用角色）

| ID | 令牌 | 亮色 | 暗色 | 出处 |
| --- | --- | --- | --- | --- |
| S01 | `slate`（secondary） | `#545E6F` | `#B3BED2`（`slateLift`） | `app/lib/ui/theme/palette.dart`:94, 98 |
| S02 | `slateContainer`（secondaryContainer） | `#E2E6F0` | `#17FFFFFF`（≈9% 白） | `app/lib/ui/theme/palette.dart`:95, 100 |
| S03 | `onSlateLift` | — | `#1A2230` | `app/lib/ui/theme/palette.dart`:99 |
| S04 | `dusk`（tertiary，暮紫，「智能体在规划」） | `#6B578F` | `#BEAEDE`（`duskLift`） | `app/lib/ui/theme/palette.dart`:103, 106 |
| S05 | `duskContainer` | `#ECE5FB` | `#3E3451` | `app/lib/ui/theme/palette.dart`:104, 108 |
| S06 | `onDuskContainer` | `#3B2C54` | `#E4DDF3` | `app/lib/ui/theme/palette.dart`:105, 109 |
| S07 | `danger`（= `error`） | `#A54743` | `#E69089`（`dangerLift`） | `app/lib/ui/theme/palette.dart`:112, 116 |
| S08 | `onDanger` | `#FDF9F8` | `#331513` | `app/lib/ui/theme/palette.dart`:113, 117 |
| S09 | `dangerContainer` | `#FFE2E0` | `#542C29` | `app/lib/ui/theme/palette.dart`:114, 118 |
| S10 | `onDangerContainer` | `#642724` | `#F8D9D7` | `app/lib/ui/theme/palette.dart`:115, 119 |
| S11 | `scrim` 遮罩基色 | `#0B111F` | `#0B111F` | `app/lib/ui/theme/palette.dart`:122 |

语义色 `DshColors` 与 accent **无关**（色相即语义），只有文字层级与代码井里子随配色换色调 〔`app/lib/ui/theme/semantic_colors.dart`:37-45, 135-137〕。

| ID | 令牌 | 亮色 | 暗色 | 出处（亮/暗同行段） |
| --- | --- | --- | --- | --- |
| M01 | `textTertiary` | `#646974` | `#9A9EA9` | `semantic_colors.dart`:183 / 274 |
| M02 | `textDisabled` | `#A1A5AC` | `#656971` | `semantic_colors.dart`:184 / 275 |
| M03 | `success` / `onSuccess` | `#337250` / `#F7FBF9` | `#8BCCA6` / `#0C2518` | `semantic_colors.dart`:186-187 / 276-277 |
| M04 | `successContainer` / `on…` | `#D9F0E1` / `#153F29` | `#233F2F` / `#C9E7D4` | `semantic_colors.dart`:188-189 / 278-279 |
| M05 | `warning` / `onWarning` | `#936123` / `#FCFAF6` | `#E6BD7C` / `#2F1E06` | `semantic_colors.dart`:190-191 / 280-281 |
| M06 | `warningContainer` / `on…` | `#F9E9CE` / `#55350D` | `#4C371D` / `#F4E2C3` | `semantic_colors.dart`:192-193 / 282-283 |
| M07 | `danger` / `onDanger` | `#A54743` / `#FDF9F8` | `#E69089` / `#331513` | `semantic_colors.dart`:194-195 / 284-285 |
| M08 | `dangerContainer` / `on…` | `#FFE2E0` / `#642724` | `#542C29` / `#F8D9D7` | `semantic_colors.dart`:196-197 / 286-287 |
| M09 | `info` / `infoContainer` / `on…` | `#2971A7` / `#DBECFC` / `#123B5A` | `#8CBDE8` / `#253C4F` / `#D1E4F4` | `semantic_colors.dart`:198-200 / 288-290 |
| M10 | `diffAddBg` / `diffAddFg` | `#DCF4E3` / `#1F613C` | `#1C3424` / `#98D5AC` | `semantic_colors.dart`:201-202 / 291-292 |
| M11 | `diffDelBg` / `diffDelFg` | `#FFE4E2` / `#923A37` | `#422423` / `#F2A7A1` | `semantic_colors.dart`:203-204 / 293-294 |
| M12 | `diffHunkFg` | `#4F5562` | `#B6BBC5` | `semantic_colors.dart`:205 / 295 |
| M13 | `codeWellBg`（代码井底） | `#EDF0F6` | `#14171E` | `semantic_colors.dart`:206 / 297 |
| M14 | `codeWellHeader` | `#E7EBF3` | `#1A1E26` | `semantic_colors.dart`:207 / 298 |
| M15 | `codeWellFg` | `#1E232E` | `#E6E9F0` | `semantic_colors.dart`:208 / 299 |
| M16 | `codeWellBorder` | `#DBDEE5` | `#2F333A` | `semantic_colors.dart`:209 / 300 |
| M17 | `lineNumberFg` | `#7D8088` | `#71747D` | `semantic_colors.dart`:210 / 301 |
| M18 | ANSI 16 色（SGR 30–37 + 90–97） | 16 值表 | 16 值表 | `semantic_colors.dart`:211-228 / 302-319 |

硬约束：所有 mono / 代码 / diff / ANSI 前景**必须**铺在 `codeWellBg` 上，不得直接放在玻璃或页面渐变上（对比度只在固定井底成立）〔`app/lib/ui/theme/semantic_colors.dart`:42-44〕。

### 1.4 强调色（默认配色 mist 的完整一套）

五套可选 accent：`mist` / `daiqing` / `dusk`（accent 变体）/ `turquoise` / `amber` 〔`app/lib/ui/theme/accent.dart`:126, 149, 171, 194, 217〕。下表为默认 `mist`：

| ID | 令牌 | 亮色 | 暗色 | 出处 |
| --- | --- | --- | --- | --- |
| A01 | `seed`（primary） | `#4B67B4` | `#97B1F0` | `app/lib/ui/theme/accent.dart`:128, 134 |
| A02 | `pressed` | `#3D569D` | `#859EDC` | `accent.dart`:129, 135 |
| A03 | `container` | `#DEE8FF` | `#2E3C5F` | `accent.dart`:130, 137 |
| A04 | `onContainer` | `#213262` | `#D8E1F6` | `accent.dart`:131, 138 |
| A05 | `onSeed` | `#FFFFFF` | `#121C34` | `accent.dart`:118, 136 |
| A06 | `bubble`（用户气泡底） | `#D9E4FD` | `#293144` | `accent.dart`:132, 139 |
| A07 | `glow`（右上光晕源色） | `#ABC3FC` | `#51679D` | `accent.dart`:133, 140 |
| A08 | `haze`（左下光晕源色） | `#D5CBEB` | `#514765` | `accent.dart`:143-144 |

### 1.5 玻璃材质（四档）

四档语义：`chrome`（浮在滚动内容上的顶栏/输入条/圆钮）、`sheet`（弹层/菜单/抽屉）、`card`（页面上的卡）、`pill`（列表里的小件）。**只有 chrome / sheet 真做模糊**，card / pill 只填半透明色 〔`app/lib/ui/theme/surfaces.dart`:8-19〕。

每条玻璃完整参数含 `fill / fallbackFill / hairline / rim / blurSigma / saturation / hairlineWidth / shadows` 〔`app/lib/ui/theme/surfaces.dart`:24-40〕；`hairlineWidth` 默认 `0.5` 〔`surfaces.dart`:33〕。

| ID | 档位 | 亮色 | 暗色 | 出处 |
| --- | --- | --- | --- | --- |
| G01 | chrome `fill` | 白 70% | `smoke` 72% | `surfaces.dart`:174 / 227 |
| G02 | chrome `fallbackFill`（关闭模糊） | `mistLowest` 96% | `smoke` 97% | `surfaces.dart`:175 / 228 |
| G03 | chrome `hairline` | `shadowInk` 8% | 白 9% | `surfaces.dart`:176 / 229 |
| G04 | chrome `rim`（高光边） | 白 85% | 白 12% | `surfaces.dart`:177 / 230 |
| G05 | chrome `blurSigma` / `saturation` | 18 / 1.3 | 18 / 1.2 | `surfaces.dart`:178-179 / 231-232 |
| G06 | chrome 阴影 | `shadowInk` 8%，blur 24，dy 8；`shadowInk` 5%，blur 3，dy 1 | 黑 35%/24/8 + 黑 25%/3/1 | `surfaces.dart`:180-183 / 233-236 |
| G07 | sheet `fill` / `fallback` | `mistLow` 82% / `mistLow` | `sheetDark` 86% / `sheetDark` | `surfaces.dart`:186-187 / 239-240 |
| G08 | sheet `hairline` / `rim` | 7% / 白 90% | 白 8% / 白 10% | `surfaces.dart`:188-189 / 241-242 |
| G09 | sheet `blurSigma` / `saturation` | 24 / 1.4 | 24 / 1.2 | `surfaces.dart`:190-191 / 243-244 |
| G10 | sheet 阴影 | 10%，blur 32，dy 8 | 黑 40%，blur 32，dy 8 | `surfaces.dart`:192 / 245 |
| G11 | card `fill` / `fallback` | 白 62% / 白 72% | 白 5% / 白 6% | `surfaces.dart`:195-196 / 248-249 |
| G12 | card `hairline` / `rim` | 6% / 白 70% | 白 7% / 白 6% | `surfaces.dart`:197-198 / 250-251 |
| G13 | pill `fill` / `fallback` | 白 55% / 白 66% | 白 6% / 白 7% | `surfaces.dart`:201-202 / 254-255 |
| G14 | pill `hairline` / `rim` | 7% / 白 75% | 白 8% / 白 8% | `surfaces.dart`:203-204 / 256-257 |
| G15 | 页面光晕 `glowPrimary` / `glowSecondary` | accent.glow α 0.42 / accent.haze α 0.38 | 0.28 / 0.24 | `surfaces.dart`:166-167 / 219-220 |
| G16 | 遮罩 `scrim` | `#0B111F` α 0.32 | 纯黑 α 0.48 | `surfaces.dart`:170, 223 |
| G17 | 用户气泡底 / 文字 | `accent.bubble` / `ink` | `accent.bubbleDark` / `paper` | `surfaces.dart`:168-169 / 221-222 |
| G18 | 玻璃总开关 `blurEnabled` | bool，关掉全部走 `fallbackFill`，不再创建 `BackdropFilter` | 同 | `surfaces.dart`:144-146 |

`smoke` / `sheetDark` 由 `NeutralTones` 按 accent 派生，源码注释给出雾蓝下 `smoke = OKLCH(0.30, 0.022, 266)`（比页面高一档的石板灰）〔`app/lib/ui/theme/surfaces.dart`:214〕。`NeutralTones` 的 OKLCH→RGB 换算实现未在本次读取范围内（`app/lib/ui/theme/oklch.dart` 未读），故 `smoke` / `sheetDark` 具体 hex 记为 **未在源码找到（派生值）**。

### 1.6 字号 / 字重 / 行高

字体策略：只打包等宽 `JetBrainsMono`，无衬线走系统（`sansFamily = null`）；**字重只用 400 / 600 两档**（AOSP 只带 Noto Sans CJK Regular，w500 会被就近映射，w600+ 走合成加粗），大号展示字用 300 〔`app/lib/ui/theme/typography.dart`:19-24, 37-43〕。

| ID | 角色 | 字号 | 字重 | 行高 | 字距 | 出处 |
| --- | --- | --- | --- | --- | --- | --- |
| T01 | `displayLarge` | 36 | w300 | 1.2 | — | `typography.dart`:189 |
| T02 | `displayMedium` | 32 | w300 | 1.2 | — | `typography.dart`:190 |
| T03 | `displaySmall`（欢迎语） | 28 | w300 | 1.25 | — | `typography.dart`:191 |
| T04 | `headlineLarge` | 26 | w600 | 1.3 | — | `typography.dart`:192 |
| T05 | `headlineMedium`（页面标题） | 22 | w600 | 1.3 | — | `typography.dart`:193 |
| T06 | `headlineSmall`（对话框标题） | 20 | w600 | 1.35 | — | `typography.dart`:194 |
| T07 | `titleLarge`（弹层标题/顶栏会话名） | 18 | w600 | 1.4 | — | `typography.dart`:195 |
| T08 | `titleMedium`（卡片/列表标题） | 16 | w600 | 1.45 | — | `typography.dart`:196 |
| T09 | `titleSmall`（工具行名/分组名） | 14 | w600 | 1.45 | — | `typography.dart`:197 |
| T10 | `bodyLarge`（**聊天正文**） | 16 | w400 | 1.65 | — | `typography.dart`:198 |
| T11 | `bodyMedium`（界面正文） | 14 | w400 | 1.55 | — | `typography.dart`:199 |
| T12 | `bodySmall`（说明/元信息） | 12 | w400 | 1.5 | — | `typography.dart`:200 |
| T13 | `labelLarge`（按钮） | 14 | w600 | 1.4 | 0.1 | `typography.dart`:201-207 |
| T14 | `labelMedium`（Chip/胶囊） | 12 | w600 | 1.35 | 0.2 | `typography.dart`:208-214 |
| T15 | `labelSmall`（时间戳/角标） | 11 | w400 | 1.35 | 0.2 | `typography.dart`:215-221 |
| T16 | 等宽正文 `mono` | 13 | 可变 | 1.55 | — | `typography.dart`:107-118 |
| T17 | 等宽小号 `monoSmall`（工具/终端输出） | 12 | 可变 | 1.5 | — | `typography.dart`:121 |
| T18 | 等宽标签 `monoLabel`（参数摘要/路径 chip） | 12 | 可变 | 1.35 | — | `typography.dart`:124-125 |
| T19 | `display()`（空态大字） | 28 | w300 | 1.25 | — | `typography.dart`:137-144 |
| T20 | `eyebrow()`（分区小标题） | 12 | w600 | 1.35 | 0.8 | `typography.dart`:157-165 |
| T21 | `wordmark()`（字标 DSH） | 18 | w600 | 1.2 | 0.6 | `typography.dart`:147-154 |
| T22 | 聊天正文字号可缩放 | `bodyLarge × scale`，用户设置建议 0.85–1.3、行高 1.5–1.9 | — | — | — | `typography.dart`:131-134, 130 |
| T23 | 行距分布 | 全部 `TextLeadingDistribution.even`；iOS 上 letterSpacing 归零，大标题 −0.2 | — | — | — | `typography.dart`:170, 81-104 |
| T24 | 等宽禁用连字 | `calt` / `liga` 关闭，`<=` `!=` `->` 保持独立字形 | — | — | — | `typography.dart`:45-47 |
| T25 | 等宽回退链 | `monospace` → `Noto Sans Mono CJK SC` → `Noto Sans CJK SC` | — | — | — | `typography.dart`:49-50 |

### 1.7 圆角

五档 + 胶囊，相邻档约 1.4 倍；全部为**连续曲率**（squircle），不是圆弧 〔`app/lib/ui/theme/shapes.dart`:9-23〕。对齐 DSH 官方 Web 的 4/8/12/16/20/28 阶梯，触屏上整体放大一档，但保留 28 作为最外层容器半径 〔`shapes.dart`:22-23〕。

| ID | 档 | 半径 | 用途 | 出处 |
| --- | --- | --- | --- | --- |
| R01 | xs | 6 | 行内代码、角标、进度条端头 | `app/lib/ui/theme/shapes.dart`:25, 15 |
| R02 | sm | 10 | Chip、工具图标块、小按钮、菜单项高亮 | `shapes.dart`:26, 16 |
| R03 | md | 14 | 卡片、代码井、输入框、菜单、主按钮 | `shapes.dart`:27, 17 |
| R04 | lg | 20 | **用户气泡**、审批卡、设置分组卡 | `shapes.dart`:28, 18 |
| R05 | xl | 28 | **输入条**、底部弹层、对话框 | `shapes.dart`:29, 19 |
| R06 | pill | 半圆端头（Stadium） | 顶栏胶囊、模型胶囊、发送键、思考胶囊 | `shapes.dart`:50, 20 |
| R07 | 带线版本 | `outlined(color, radius: md, width: 1)` | 需要描边的连续圆角 | `shapes.dart`:53-56 |

### 1.8 间距阶梯

| ID | 令牌 | 值 | 用途 | 出处 |
| --- | --- | --- | --- | --- |
| P01 | `xxs` / `xs` / `sm` | 2 / 4 / 8 | 紧凑行内 | `app/lib/ui/theme/shapes.dart`:63-65 |
| P02 | `md` / `lg` / `xl` | 12 / 16 / 20 | 常规块间 | `shapes.dart`:66-68 |
| P03 | `xxl` / `xxxl` | 24 / 32 | 分区间 | `shapes.dart`:69-70 |
| P04 | `gutter` | 16 | 页面水平留白（手机） | `shapes.dart`:73, 61 |
| P05 | `inTurn` | 8 | 同一轮内部（工具行 ↔ 正文） | `shapes.dart`:76 |
| P06 | `betweenTurns` | 24 | 换轮（上一轮回答 → 下一条用户消息） | `shapes.dart`:79 |
| P07 | `readingWidth` | 720 | 正文最大阅读宽度，居中（官方 Web 为 748px） | `shapes.dart`:83, 81-82 |
| P08 | `floatInset` | 12 | 浮在阅读列上的控件（输入条、待办条、空态建议）距阅读列两边 | `shapes.dart`:87 |
| P09 | `floatingWidth` | 696 | `readingWidth − 2 × floatInset` | `shapes.dart`:90 |

### 1.9 阴影 / 边框

顺色规则：阴影一律用带蓝的墨色（`shadowInk` / `shadowNight`），不用纯黑 〔`palette.dart`:70-71, 90〕。具体阴影值见 §1.5（玻璃四档）与 §3.4（错误横幅）。

| ID | 令牌 | 值 | 出处 |
| --- | --- | --- | --- |
| B01 | 发丝描边默认宽 | 0.5 | `app/lib/ui/theme/surfaces.dart`:33 |
| B02 | 卡片描边宽 | 1（`outlined` 默认 width） | `app/lib/ui/theme/shapes.dart`:53-55 |
| B03 | 错误横幅描边 | 0.5，色 = accent α 0.3 | `app/lib/ui/chat/widgets/notice_row.dart`:239 |
| B04 | 模型卡描边 | 1.5 | `app/lib/ui/chat/model_sheet.dart`:887 |
| B05 | 主题包允许的描边下限 | `borderWidth.clamp(0.8, 2.0)`（用户风格包路径） | `app/lib/ui/chat/widgets/user_bubble.dart`:146 |

### 1.10 动效时长与曲线

原则：反馈 ≤160ms、结构 240–340ms、环境 2.4s；系统"移除动画"时取时长为 0 〔`app/lib/ui/theme/motion.dart`:10-17, 183-186〕。

| ID | 令牌 | 值 | 用途 | 出处 |
| --- | --- | --- | --- | --- |
| A-N01 | `press` | 90ms | 按压缩放、水波起始 | `motion.dart`:20 |
| A-N02 | `quick` | 160ms | 淡入淡出、图标交叉切换、复制成功 | `motion.dart`:23 |
| A-N03 | `standard` | 240ms | 小块展开收起、列表项进出 | `motion.dart`:26 |
| A-N04 | `collapse` | 280ms | 思考块/工具行折叠展开 | `motion.dart`:29 |
| A-N05 | `emphasized` | 340ms | 胶囊变形（顶栏展开、模型选择）、抽屉落位 | `motion.dart`:32 |
| A-N06 | `sheetIn` / `sheetOut` | 380ms / 260ms | 底部弹层进出 | `motion.dart`:35-36 |
| A-N07 | `glassIn` | 120ms | 弹层停稳后模糊透出 | `motion.dart`:39 |
| A-N08 | `slow` | 480ms | 页面级过场 | `motion.dart`:42 |
| A-N09 | `breath` | 2400ms | 环境呼吸（连接中状态点） | `motion.dart`:45 |
| A-N10 | `page` / `push` | 300ms / 300ms | 步骤切换 / 二级页水平推入 | `motion.dart`:146, 149 |
| A-N11 | `stagger` / `staggerLimit` | 30ms / 8 项 | 首次出现逐项错开 | `motion.dart`:155-156 |
| A-N12 | `rise`（首次上移） | 8 | 列表首现位移 | `motion.dart`:159 |
| A-N13 | `pressScale` | 0.97 | 按压缩放（松手回到 1，不越过） | `motion.dart`:162 |
| A-N14 | `std` 曲线 | `Easing.standard`（≈ cubic-bezier(.4,0,.2,1)） | 标准缓动 | `motion.dart`:48, 47 |
| A-N15 | `enter` / `exit` | emphasizedDecelerate / emphasizedAccelerate | 进场快出慢停 / 退场慢起快走 | `motion.dart`:51, 54 |
| A-N16 | `collapseCurve` | `Curves.easeInOutCubic` | 折叠展开 | `motion.dart`:57 |
| A-N17 | `glide` | `Cubic(0.32, 0.72, 0.0, 1.0)` | 抽屉/胶囊"跟手后落位" | `motion.dart`:60 |
| A-N18 | `sheetRise` / `sheetFall` | `Cubic(0.2, 0.9, 0.25, 1.0)` / `easeOutCubic` | 弹层升起 / 收起 | `motion.dart`:165, 168 |
| A-N19 | `pressSpring` | 150ms，bounce 0（默认） | 按压落位 | `motion.dart`:64 |
| A-N20 | `layoutSpring` | 350ms | 尺寸/位置变化落位 | `motion.dart`:67 |
| A-N21 | `sheetSpring` | 500ms | 跟手后落位 | `motion.dart`:70 |
| A-N22 | `flingSpring` | 500ms，bounce 0.15 | 甩出的抽屉 | `motion.dart`:72-76 |
| A-N23 | 灵动档弹簧 | release 320/0.45、pop 380/0.20、thumb 320/0.15、rise 420/0.18 | 只喂位移与缩放，不喂透明度 | `motion.dart`:78-102 |
| A-N24 | 回弹位移距离 | 36，配 scale 0.94（刚发出的气泡从输入框升起） | | `app/lib/ui/chat/widgets/stream_motion.dart`:76-77 |

---

## 2 布局骨架

### 2.1 消息列表

**结构**：居中锚定的 `CustomScrollView`，视口切成两段 sliver，`center` 在两段之间；`before` 段是打开会话时已有的行（向上长），`after` 段是新来的行（向下长，流式只发生在这段）〔`app/lib/ui/chat/chat_list.dart`:67-82〕。跟随策略是"贴底时同一次布局里把位置挪到新末尾"，离底较远时由帧后 `jumpTo(maxScrollExtent)` 追过去 〔`chat_list.dart`:83-86〕。

| ID | 项 | 值 | 出处 |
| --- | --- | --- | --- |
| Y01 | 正文列最大宽 | 720（`AppSpace.readingWidth`），超宽居中：左右各留 `(maxWidth − 720)/2` | `app/lib/ui/chat/chat_list.dart`:1105, 1053；`app/lib/ui/theme/shapes.dart`:83 |
| Y02 | 顶部留白 | `topInset + 8` | `chat_list.dart`:1014 |
| Y03 | 底部留白 | `bottomInset + 8 + headroom` | `chat_list.dart`:973 |
| Y04 | `bottomInset` 语义 | 视口伸到输入条上沿之下多少（常量） | `chat_list.dart`:114-117 |
| Y05 | 用户消息→用户消息 | 8（`inTurnGap`） | `chat_list.dart`:1140；`shapes.dart`:76 |
| Y06 | 用户消息→上一条非用户 | 24（`turnGap`） | `chat_list.dart`:1140；`shapes.dart`:79 |
| Y07 | 过程行之间 | `AppSpace.md` = 12 | `chat_list.dart`:1143 |
| Y08 | 回合脚注行前 | `AppSpace.xs` = 4 | `chat_list.dart`:1145 |
| Y09 | 回到底部按钮位置 | 右 `max(0,(width−720)/2)+12`，底 `bottomInset+12` | `chat_list.dart`:1053-1054 |
| Y10 | 贴底判定的最小空档 | `AppSpace.sm` = 8 | `chat_list.dart`:176 |

**用户气泡**：右对齐的软气泡，比页面深一档，像"按进去"的 〔`app/lib/ui/chat/widgets/user_bubble.dart`:19〕。

| ID | 项 | 值 | 出处 |
| --- | --- | --- | --- |
| Y11 | 最大宽度 | 可用宽的 **82%** | `user_bubble.dart`:174, 190 |
| Y12 | 内边距 | 水平 16 / 垂直 8 | `user_bubble.dart`:166 |
| Y13 | 圆角 | lg = 20（连续曲率） | `app/lib/ui/theme/shapes.dart`:28, 41 |
| Y14 | 气泡底色 / 文字色 | `surfaces.userBubble`（=`accent.bubble`）/ `surfaces.onUserBubble`（=`ink`） | `user_bubble.dart`:106-108, 136；`surfaces.dart`:168-169 |
| Y15 | 正文样式 | `AppType.chatBody` = `bodyLarge` 16/1.65 | `user_bubble.dart`:110；`typography.dart`:131-134, 198 |
| Y16 | 附件与文字间距 | 8；附件同样受 82% 宽度约束、右对齐 | `user_bubble.dart`:187, 182-186 |
| Y17 | 引文块与正文间距 | 6 | `user_bubble.dart`:128 |
| Y18 | 乐观态（未回显）透明度 | 0.72，过渡 `quick`(160ms) | `user_bubble.dart`:185, 191-194 |
| Y19 | 状态行（发送中/排队中） | 图标 12 + 4 间距 + `labelSmall` 三级色；顶端 4、右端 8 | `user_bubble.dart`:277, 286-288 |
| Y20 | 状态行文字操作 | `TextButton` minHeight 32、水平 padding 8、`labelSmall` w600 | `user_bubble.dart`:294-300 |
| Y21 | 生成参数行 | 气泡→行 4，行右端缩进 8 | `user_bubble.dart`:318, 321 |
| Y22 | 气泡内文字禁配 | 只用主文字/次级文字；三级文字(4.32)与主色(4.22)不过 AA | `user_bubble.dart`:21-22 |

**助手消息**：无框裸排的全宽正文，**没有头像、没有署名** 〔`app/lib/ui/chat/widgets/assistant_message.dart`:36-44〕。

| ID | 项 | 值 | 出处 |
| --- | --- | --- | --- |
| Y23 | 对齐 | 左对齐，占满正文列宽 | `assistant_message.dart`:36, 194-196 |
| Y24 | 思考行 → 正文 | 8 | `assistant_message.dart`:199 |
| Y25 | 思考后第一段正文前距 | 8（随淡入从 0 长到全高，200ms） | `assistant_message.dart`:406, 34 |
| Y26 | 中断标记 | 正文末尾，上距 8，图标 16 + 4 间距，`bodySmall` 高度 1.3，danger 色 | `assistant_message.dart`:217-219, 418-428 |
| Y27 | 操作行出现 | 只给一轮的最后一条正文；`ExpandReveal` 240ms + `AppMotion.std` | `assistant_message.dart`:193, 217-240 |
| Y28 | 操作行按钮 | 图标 20，按钮 36×36，行内顶端 4，脚注前间距 8 | `app/lib/ui/chat/widgets/message_actions.dart`:104, 212, 194, 201 |
| Y28b | 脚注容器 | 最小高 36，内边距 `LTRB(8,4,4,4)`（左 8 贴正文左缘，右 4 为箭头 icon 的光学补偿） | `app/lib/ui/chat/widgets/turn_footnote.dart`:62-67 |

**头像 / 时间戳 / 署名位置**：dsh-Go 的消息本体**不显示**头像、时间戳、署名 〔`assistant_message.dart`:36；`user_bubble.dart`:19-32 无头像相关字段〕。会话层面的时间信息出现在**回合脚注**：`47 秒 · 12 步 · 约 ¥0.03 · 36 万 tokens ›`，挂在操作行右端，样式 `labelSmall` 12 / 三级色 / 行高 1.3 / 等宽数字 〔`app/lib/ui/chat/widgets/turn_footnote.dart`:18, 25, 49-51〕。若 NeoChat 需要逐条消息时间戳，属**新增**，dsh-Go 无可抄样式（**未在源码找到**）。

### 2.2 输入栏（composer）

输入条是 chrome 玻璃 + 28 圆角，1–6 行自动增高，右下角发送/停止 〔`app/lib/ui/chat/composer.dart`:30-31, 4-6〕。两颗圆键是胶囊的"端盖" 〔`composer.dart`:32-34〕。

| ID | 项 | 值 | 出处 |
| --- | --- | --- | --- |
| Y29 | 圆角 | 关闭态 `AppShape.xl` = 28；输入时展开为 `AppShape.lg` = 20 | `composer.dart`:646, 653 |
| Y30 | 键尺寸 `_keySize` | 36 | `composer.dart`:174 |
| Y31 | 键到胶囊边 `_keyGap` | 6 | `composer.dart`:175 |
| Y32 | 单行胶囊总高 | **48**（= 36 + 6 + 6，算术结果，**推断**；注释声称"高 48、圆角 28 = 键半径 20 + 8"，与代码 `_keySize=36` 不一致，以代码算术为准） | `composer.dart`:174-175, 602, 32-34 |
| Y33 | 内边距 | `fromLTRB(leading == null ? 18 : 6, 6, 6, 6)` | `composer.dart`:602 |
| Y34 | 输入字号 / 行高 | 15 / 1.45（不是 16/1.65） | `composer.dart`:178, 28 |
| Y35 | 单行文字垂直居中留白 | `max(4, (36 − 字号×1.45)/2)` | `composer.dart`:388-389 |
| Y36 | 最大可见行数 | 6（多行仍可滚动）；页面级另有 `linesFor()`：六行、或键盘上方 2/5 高度能放的行数，且不少于 2 | `composer.dart`:66, 137, 157-163 |
| Y37 | 键间最小间距 | 8（`leadingKey` 后、accessory 前 2） | `composer.dart`:538-541 |
| Y38 | 附着区（tray）内边距 | `fromLTRB(6, 2, 6, 6)` | `composer.dart`:578, 586 |
| Y39 | 引文条间距 | `quoteTop` 4 / `quoteGap` 4 | `app/lib/ui/chat/widgets/quote_chip.dart`:126, 130 |
| Y40 | 引文胶囊 | 最小高 32、字形 16、圆形半径 16、文字 14/1.3 次级色 | `quote_chip.dart`:28, 31, 38 |
| Y41 | 状态行（`footer`） | 由页面传入的一条，贴在键行旁；输入时胶囊展开为圆角矩形、文字独占一行、键与 footer 在下方一行，键盘收起后复原 | `composer.dart`:147-158, 699-742 |
| Y42 | 静态几何（展开态两行） | 文字与行的间距 8、行内件间距 = `_keyGap` 6、文字独占行的内缩 8、文字与下一行间距 2 | `composer.dart`:732-742 |

**发送 / 停止按钮**（同一个 36 圆键，四种模式：send / stopRun / mic / stopVoice）〔`composer.dart`:848-863〕：

| ID | 项 | 值 | 出处 |
| --- | --- | --- | --- |
| Y43 | 底色 | 可用：`primary`；禁用：`onSurface` 12%；麦克风/次要：`onSurface` 6% | `composer.dart`:903-905 |
| Y44 | 前景 | 可用：`onPrimary`；禁用：`onSurface` 38% | `composer.dart`:905-906 |
| Y45 | 图标尺寸 | send / mic 20；stop 18 | `composer.dart`:928-931 |
| Y46 | 按下反馈 | scale 0.9，`press`(90ms) 或灵动档 `thumbCurve` | `composer.dart`:938-940 |
| Y47 | 忙碌态 | 圆形进度：内边距 11、strokeWidth 2 | `composer.dart`:964-966 |
| Y48 | 长按 | 触发 mediumImpact 触感；生成中长按可选"插话 / 停下先处理 / 排队" | `composer.dart`:107, 946-950 |
| Y49 | 停止键让位 | 有审批/提问在等时停止键退为次要（quiet），主操作交给卡片按钮 | `composer.dart`:121-124, 881 |

### 2.3 顶部栏

| ID | 项 | 值 | 出处 |
| --- | --- | --- | --- |
| Y50 | 栏高 `barHeight` | 56 | `app/lib/ui/chat/chat_header.dart`:89 |
| Y51 | 标题胶囊高 | 44（大字时按文字放大）；栏高 = `max(56, 胶囊高 + 12)` | `chat_header.dart`:91-98 |
| Y52 | 整块高度 | 状态栏 inset + 栏高 + 8 | `chat_header.dart`:100 |
| Y53 | 水平内边距 | 12 | `chat_header.dart`:186 |
| Y54 | 会话名 | `titleSmall` 覆盖为 15 / 1.3 | `chat_header.dart`:158 |
| Y55 | 状态文字 | `labelSmall` 覆盖为 11 / 1.25 | `chat_header.dart`:159 |
| Y56 | 状态圆点 | 直径 6，与文字间距 5 | `chat_header.dart`:362-363 |
| Y57 | 左右圆钮图标 | 菜单 22 / 通话 21 / 新对话 22 | `chat_header.dart`:210, 319, 341 |
| Y58 | 标题与模型胶囊共享剩余宽度 | 标题胶囊左 8 起于左键，尾部件贴右键 | `chat_header.dart`:406-408 |
| Y59 | 模型胶囊高 | 最小 44（chrome 玻璃 + Stadium） | `app/lib/ui/chat/model_sheet.dart`:163-164 |
| Y60 | 模型胶囊内边距 / chevron | 左 12、右 8、chevron 16；`compactWidth = 12 + 16 + 8 = 36` | `model_sheet.dart`:133-138 |
| Y61 | 模型胶囊文字 | `labelMedium` 覆盖为 13；推理档位后缀用 `onSurfaceVariant` + w400，与名称间距 5 | `model_sheet.dart`:151, 173, 191-198 |
| Y62 | 胶囊最小文字宽 / 最大文字宽 | `2.5em` / `10em`（em = 13 × 文字缩放） | `model_sheet.dart`:176-178, 148 |

---

## 3 状态与动效

### 3.1 流式输出

| ID | 状态 | 规格 | 出处 |
| --- | --- | --- | --- |
| ST01 | 末尾光标 | 呼吸竖线，宽 2.5、高 18（建议取正文行高 ~72%）、圆角 1.5、主色；不透明度 1.0 ↔ 0.25，周期 1000ms，`AppMotion.std` 曲线；以 `WidgetSpan(PlaceholderAlignment.middle)` 嵌在正文末尾，跟随换行且不破坏可选性 | `app/lib/ui/common/streaming_cursor.dart`:9-23, 29-66 |
| ST02 | 光标落点 | 正在生成的末尾；正文末尾是表格行时放在最后一个 `|` 之前；末尾是代码围栏时放在围栏内 | `app/lib/ui/chat/render/markdown_body.dart`:591-605 |
| ST03 | 停顿点（长等待） | 5×5 圆点，正文下一行起始处；静止 900ms 后出现，呼吸周期 = `breath/2` = 1200ms，不透明度 0.25 ↔ 1.0，三级色；新文字到达即消失 | `app/lib/ui/chat/widgets/assistant_message.dart`:443-446, 451-455, 492-503 |
| ST04 | 等待首个 token | 20px 呼吸思考光球（`ThinkingOrb`，`OrbState.breathing`），与一行字同高 | `app/lib/ui/common/typing_indicator.dart`:5-24 |
| ST05 | 三点 → 内容的交接 | 出 90ms 线性淡出（原地），入 `quick`(160ms) + 3px 上移，曲线 `Cubic(0.23, 1, 0.32, 1)`；交接槽最小高 = 一行正文高（16 × 1.65 × 文字缩放） | `assistant_message.dart`:28-31, 336-378 |
| ST06 | 思考行标签扫光 | 亮度从标签灰扫到正文深，不换色、不位移；开始思考 600ms 后第一次，每次扫 1s | `app/lib/ui/chat/widgets/thinking_pill.dart`:229-246 |
| ST07 | 思考行的三个动效分工 | 实时窗口是信息不动；扫光是主角；图形转得很慢 | `thinking_pill.dart`:237-238 |
| ST08 | 思考实时窗口 | 最多三行连续文字，跟最新字走，高度固定（列表不因思考变长而跳）；点行展开全文，轮结束自动收回 | `thinking_pill.dart`:37-42 |
| ST09 | 思考图形 | 进行中：光球 18/20px；结束后：静态 `ThinkGlyph` 15；品牌蓝 `#4D6BFE`（暗色与白 lerp 0.3），底 10%/18% 透明度 | `thinking_pill.dart`:52, 133, 146, 153-154 |
| ST10 | 流式渲染的节流 | 列表每 32ms 重建一次；同一段文字只建一次 Markdown 子树 | `assistant_message.dart`:134-136 |

### 3.2 加载态

| ID | 状态 | 规格 | 出处 |
| --- | --- | --- | --- |
| ST11 | 发送键忙碌 | 键内圆形进度（padding 11，stroke 2），键整体 `busy` 期间不可点 | `composer.dart`:962-968 |
| ST12 | 状态圆点呼吸 | 直径默认 8，busy 时透明度 1 ↔ 0.35，周期 1200ms；换状态时颜色 240ms 过渡 | `app/lib/ui/common/status_dot.dart`:28-46, 53-60；`app/lib/ui/common/status_chip.dart`:12-33 |
| ST13 | 状态语义色 | ok=success、busy=primary、warn=warning、error=danger、idle=outline；**颜色不是唯一信号**，旁边必须配文字 | `status_dot.dart`:10-31, 38-47 |
| ST14 | 列表首现动画 | 淡入 + 上移 8 + 高度从 0 长到全高，只播一次，逐项错开 30ms（最多 8 项） | `app/lib/ui/chat/widgets/stream_motion.dart`:49-55, 85-88 |
| ST15 | 模型弹层加载 | 高度 120 的加载块 | `app/lib/ui/chat/model_sheet.dart`:1013-1018 |

### 3.3 空态

| ID | 项 | 值 | 出处 |
| --- | --- | --- | --- |
| ST16 | 左右边距 / 最大宽 | 12 / 696（与顶栏胶囊、输入条同一列） | `app/lib/ui/chat/empty_state.dart`:202-203 |
| ST17 | 问候语 | `displaySmall` 28 / w300 / 1.25（紧凑布局用 `headlineMedium` 22） | `empty_state.dart`:228-230；`typography.dart`:191, 193 |
| ST18 | 副标题 | 4 间距 + `bodyLarge` 三级色 | `empty_state.dart`:317-326 |
| ST19 | 副标题 → 胶囊 | 12（紧凑 10） | `empty_state.dart`:206, 323 |
| ST20 | 胶囊 → 选项行 | 8 | `empty_state.dart`:209, 331 |
| ST21 | 选项行 → 建议 | 28（紧凑 14） | `empty_state.dart`:212, 362 |
| ST22 | 建议 → 提示行 | 6 | `empty_state.dart`:215, 426 |
| ST23 | 主胶囊 | 高 36，水平 padding 14 + 4，垂直 6，`bodyMedium` 14/1.55 w600，primary 底 + onPrimary 字 | `app/lib/ui/chat/empty_actions.dart`:30-36, 138-143 |
| ST24 | 安静 chip | 高 36，图标 16 + 间距 6，chevron 18；未选底 = 暗色白 7% / 亮色 onSurface 5% | `empty_actions.dart`:38-48, 59-61 |
| ST25 | 建议 chip 间距 | 8（横竖同值） | `empty_actions.dart`:51 |
| ST26 | 建议 chip 外形 | `AppShape.card`（md=14）容器，内边距 8（紧凑）/10 | `empty_state.dart`:383-386 |

### 3.4 错误态

两种形态并存，用途不同：

| ID | 形态 | 规格 | 出处 |
| --- | --- | --- | --- |
| ST27 | 横幅 `ErrorBanner` | 底 = `dangerContainer` α0.92 混合 `surface`；圆角 md=14；描边 0.5 / accent α0.3；阴影 `shadowInk` 8%、blur 16、dy 4；内边距 `LTRB(12,4,4,4)`；图标 17 + 间距 8；文字 `bodySmall` `onDangerContainer`，最多 3 行省略；操作 `TextButton` min 48×48；关闭按钮 40×40 / 图标 18 | `app/lib/ui/chat/widgets/notice_row.dart`:209-282 |
| ST28 | 提示变体 | `info=true` 时换成 `infoContainer` / `info` / `onInfoContainer`，图标换 info | `notice_row.dart`:209-233 |
| ST29 | 行式错误 `NoticeRow` | 出错时图形块用 `dangerContainer` 浅底，尺寸 `StreamRow.lead × 0.6 = 14.4`；文字 `StreamRow.detail` 覆盖 height 1.45 + danger 色；展开详情左内缩 `StreamRow.indent = 32`、底部 8 | `notice_row.dart`:42, 103, 135-142, 171 |
| ST30 | 无障碍 | 横幅是 `liveRegion: true`，读屏立即播报 | `notice_row.dart`:234-235 |
| ST31 | 错误码 → 短句 | 按 code 映射（AUTH / QUOTA / RATE_LIMIT / CONTEXT_WINDOW_EXCEEDED / SERVER / HTTP_5xx / TIMEOUT / TRANSPORT / EMPTY_RESPONSE / CLIENT_UPGRADE_REQUIRED / MODEL_UNSUPPORTED / UNSUPPORTED_REASONING_EFFORT …），后端原文只放详情 | `app/lib/ui/chat/widgets/turn_failure_text.dart`:14-34 |
| ST32 | 未知结果横幅 | 单独的 `unknown_results_banner` 组件 | `app/lib/ui/chat/widgets/unknown_results_banner.dart`（存在，本次未读内容） |
| ST33 | 轻提示 | `composer_toast`：贴着输入条的短提示 | `app/lib/ui/chat/composer_toast.dart`:58 行（本次未读细节） |

### 3.5 长按菜单

| ID | 项 | 值 | 出处 |
| --- | --- | --- | --- |
| ST34 | 触发 | 长按气泡或正文；或点回复操作行末尾 `⋯`；同一张表 | `app/lib/ui/chat/widgets/message_menu.dart`:1-6, 73-77 |
| ST35 | 结构 | 顶部预览 → 第一组（本条消息：复制/选择文字/引用）→ 第二组（会开新会话的：分享/朗读/重新回答/从这里分叉） | `message_menu.dart`:73-77；`assistant_message.dart`:294-302 |
| ST36 | 预览位置 | 左右内缩 = `kSheetGutter + 4 = 24`，距下方内容 10 | `message_menu.dart`:182, 193 |
| ST37 | 预览文字 | `bodyMedium` + `onSurfaceVariant` | `message_menu.dart`:231 |
| ST38 | 图片预览 | 56×56 缩略图（圆角 sm=10）+ 12 间距 + 名称 | `message_menu.dart`:243, 254, 267 |
| ST39 | 组间 / 组末间距 | 组卡之间 12；整表底部 12 | `message_menu.dart`:210, 214 |
| ST40 | 弹层外壳 | 顶部圆角 xl=28；左右 gutter 20（`kSheetGutter = SheetTitleBar.inset`） | `app/lib/ui/common/app_sheet.dart`:222, 29；`app/lib/ui/common/sheet_title_bar.dart`:31 |
| ST41 | 拖拽把手 | 上内边距 10、把手 36×4、圆端、色 `onSurfaceVariant` α0.35、下内边距 12 | `app/lib/ui/common/app_sheet.dart`:335-341 |
| ST42 | 拖拽关闭阈值 | 甩速 > 700 或拖过自身高度 50%；落位用 `sheetSpring`(500ms) | `app_sheet.dart`:251-252, 281, 287 |
| ST43 | 标题栏 | 可见边缘对齐 gutter 20；按钮 32（点按区 44） | `sheet_title_bar.dart`:31-36, 363 |
| ST44 | 菜单高度策略 | 按行数自适应（最高到屏幕），不是默认 9/16，避免最后几行被藏 | `message_menu.dart`:91-92 |

### 3.6 过程行解剖（思考/工具/通知共用）

| ID | 项 | 值 | 出处 |
| --- | --- | --- | --- |
| ST45 | 图形列宽 `lead` | 24（24×24 居中） | `app/lib/ui/chat/widgets/stream_row.dart`:21, 72-80 |
| ST46 | 图形与文字间距 `gap` | 8 | `stream_row.dart`:24 |
| ST47 | 文字列起点 `indent` | 32 | `stream_row.dart`:27 |
| ST48 | 单行最小高 | 32（24 图形 + 上下各 4） | `stream_row.dart`:30, 57-59 |
| ST49 | 点按高亮外溢 `bleed` | 横向 8 | `stream_row.dart`:33, 63-64 |
| ST50 | 行标签 / 行内细节 | `bodyMedium` 次级色 1.3 / `bodySmall` 13 三级色 1.3 | `stream_row.dart`:35-43 |

---

## 4 组件清单（含双端实现要点）

| 组件 | dsh-Go 对应文件 | Compose 实现要点 | WinUI 3 实现要点 |
| --- | --- | --- | --- |
| 用户气泡 | `app/lib/ui/chat/widgets/user_bubble.dart` | `Column(horizontalAlignment = End)` + `widthIn(max = columnWidth * 0.82f)`；`Surface(color = accentBubble, shape = RoundedCornerShape(20.dp))`；内边距 `Padding(16.dp, 8.dp)`；文字用 `LocalTextStyle` 的 bodyLarge（16/1.65，`lineHeightStyle = LineHeightStyle(Center, Proportional)`）。 | `Border(CornerRadius="20", Background=<bubble brush>)`，`MaxWidth` 用 `Column` 宽度比例换算（WinUI 无百分比，需在 `SizeChanged` 里算 `0.82 * available`）；`TextBlock` 设 `FontSize=16`、`LineHeight=26.4`、`LineStackingStrategy=BlockLineHeight`。 |
| 助手正文 | `app/lib/ui/chat/widgets/assistant_message.dart` | 无容器，直接 `Text`/`AnnotatedString`；分段间距用 `Column(verticalArrangement = spacedBy(8.dp))`；中断标记 `Row` + `Icon(16.dp)` + `bodySmall`。 | 无容器 `RichTextBlock`/`TextBlock`；段间距用 `StackPanel(Spacing=8)`；中断标记 `StackPanel(Orientation=Horizontal, Spacing=4)`。 |
| 思考折叠行 | `app/lib/ui/chat/widgets/thinking_pill.dart` | 复用过程行几何（24/8/32）；`AnimatedVisibility` 折叠 280ms + `easeInOutCubic`；实时三行窗口用固定高度 `Text(maxLines=3)` + 自动跟随最后一行；扫光用 `drawText` + `Shader`（API 31+）或降级为纯色。 | `Expander`/自定义 `ToggleButton` + `Storyboard` 高度动画；三行窗口用固定 `Height` 的 `TextBlock` + `ScrollViewer.ChangeView`；扫光需 Composition `LinearGradientBrush` 遮罩，可降级。 |
| 过程行外壳 | `app/lib/ui/chat/widgets/stream_row.dart` | `Row` + `Box(24.dp)` + `Spacer(8.dp)` + 文本列；`heightIn(min = 32.dp)`；`clickable` 的 ripple 用 `indication` 外扩 8，不复用 `Modifier.clickable` 的默认裁剪。 | `Grid` 两列（24 / *）+ `Margin`；`MinHeight=32`；悬停/按压高亮用 `PointerEntered` + `Rectangle` 外扩 8（WinUI 无 ripple）。 |
| 输入栏 | `app/lib/ui/chat/composer.dart` | `Surface(shape = RoundedCornerShape(28.dp), color = glassChrome)`；`Row` 内：附件键 36、`BasicTextField(maxLines=6)` 15sp/1.45、发送键 36；`Modifier.imePadding()`；展开态用 `animateDpAsState` 在 28→20 之间切圆角。 | `Grid` 或 `RelativePanel` + `Border(CornerRadius=28)`；`TextBox(AcceptsReturn=True, MaxHeight=6*行高)`；`MaxHeight` 由 6 行算；`RoundedCornerRadius` 用 `CornerRadius`；键盘避让用 `InputPane`。 |
| 发送/停止键 | `app/lib/ui/chat/composer.dart`（`_SendButton`） | `Box(36.dp, clip = CircleShape)` + `Icon(20.dp)`；`graphicsLayer(scaleX/Y = 0.9f)` 按压；忙碌用 `CircularProgressIndicator(strokeWidth = 2.dp)`。 | `Button` 重模板为 `Ellipse` 底 + `FontIcon`；按压用 `ScaleTransform` + `Storyboard`；忙碌用 `ProgressRing(IsIndeterminate=True)`。 |
| 模型选择入口 | `app/lib/ui/chat/model_sheet.dart`（`ModelChip` / `_ModelCard`） | chrome 玻璃 `Surface(StadiumShape)`，`heightIn(min = 44.dp)`，`padding(start = 12.dp, end = 8.dp)`，`Icon(16.dp)`；弹层里卡片 `RoundedCornerShape(20.dp)`、内边距 16、描边 1.5、卡间 12、选中标记 20。 | `Button` 模板 → `Border(CornerRadius="22")`（半圆端）；弹层 `ContentDialog` 或自定义 `Popup`，卡片用 `Grid` + `Border(CornerRadius="20", BorderThickness="1.5")`。 |
| 顶部栏 | `app/lib/ui/chat/chat_header.dart` | `Box(height = 56.dp)` + `Row`（左 36/44 圆钮、中间标题胶囊 `heightIn(min=44)`、右模型胶囊）+ `padding(horizontal = 12.dp)`；滚动遮罩淡入用 `Brush.verticalGradient`。 | `Grid` + `MinHeight=56`；胶囊 `Border(CornerRadius="22")`；状态点用 `Ellipse(6)` + `DoubleAnimation` 透明度。 |
| 错误提示 | `app/lib/ui/chat/widgets/notice_row.dart`（`ErrorBanner` / `NoticeRow`） | `Surface(color = dangerContainer α0.92 over surface, shape = RoundedCornerShape(14.dp), border = BorderStroke(0.5.dp, danger α0.3))`；`Row` + `Icon(17.dp)` + `Text` + `TextButton`。 | `InfoBar` 可用但样式难对齐：更稳的是自建 `Border` + `Grid`；`AutomationProperties.LiveSetting=Assertive` 替代 `liveRegion`。 |
| 空态 | `app/lib/ui/chat/empty_state.dart`、`empty_actions.dart` | `Column(horizontalAlignment = Center)` 限宽 696；问候语 `displaySmall`；胶囊 `height(36.dp)` + `StadiumShape`；建议 chip 用 `FlowRow`（Compose 1.7+）或 `LazyVerticalGrid`。 | `StackPanel` + `MaxWidth=696`；胶囊 `Button` + `CornerRadius="18"`；chip 流式排布用 `ItemsRepeater` + `UniformGridLayout`。 |
| 长按菜单 | `app/lib/ui/chat/widgets/message_menu.dart`、`app/lib/ui/common/app_sheet.dart` | `ModalBottomSheet(shape = RoundedCornerShape(topStart/End = 28.dp))`；预览 `padding(horizontal = 24.dp)`；行用 `ListItem` 统一 48 高。 | `ContentDialog` 高度难控；建议自建 `Popup` + `Border(CornerRadius="28,28,0,0")` + `ScrollViewer`。 |
| 流式光标 | `app/lib/ui/common/streaming_cursor.dart` | `AnnotatedString` 末尾插 `InlineTextContent`（`placeholder`）；宽度固定 2.5dp、高 18dp、圆角 1.5dp；`rememberInfiniteTransition` 在 1.0 ↔ 0.25 之间往返 1000ms。 | `TextBlock.Inlines` 插 `InlineUIContainer`（含 `Border`）；`DoubleAnimation` `AutoReverse=True`、`Duration=0:0:1`。 |
| 等待指示 | `app/lib/ui/common/typing_indicator.dart` | 优先用三个 6dp 圆点错峰缩放（dsh-Go 的光球是自定义 shader，双端不建议复刻）；见 §5 差异 6。 | 同上；`Storyboard` 驱动三个 `Ellipse` 的 `Opacity`/`ScaleTransform`。 |
| 状态圆点 | `app/lib/ui/common/status_dot.dart` | `Box(8.dp, CircleShape)` + `animateFloatAsState` 透明度 1↔0.35 `infiniteRepeatable(1200ms)`。 | `Ellipse` + `DoubleAnimation`（`RepeatBehavior=Forever`, `AutoReverse=True`）。 |
| 引用条 | `app/lib/ui/chat/widgets/quote_chip.dart` | `Surface(StadiumShape, heightIn(min = 32.dp))`，左右各一个 32dp 方形槽装引号/关闭字形 16dp。 | `Border(CornerRadius="16")` + 两个 `Button`（`MinWidth/Height=32`）。 |
| 回合脚注 | `app/lib/ui/chat/widgets/turn_footnote.dart` | `Text` + `fontFeatureSettings = "tnum"`，12sp 三级色，1.3 行高；`InkWell` 打开轨迹。 | `TextBlock` 设 `Typography.NumeralStyle=Tabular`（或 `FontFeature` 不支持时用等宽字），12epx。 |

---

## 5 差异与取舍（Flutter → Compose / WinUI 3）

| # | dsh-Go 用到的东西 | 出处 | 双端替代方案 | 还原度影响 |
| --- | --- | --- | --- | --- |
| 1 | **连续曲率圆角（squircle）**：`SquircleBorder` 自定义 `ShapeBorder`，全站五档都用它 | `app/lib/ui/theme/shapes.dart`:38-56；`app/lib/ui/theme/squircle.dart`（未读内容） | Compose：`RoundedCornerShape` 是四分之一圆弧；要接近连续曲率需自绘 `Path`（三次贝塞尔，曲率连续近似）或用 `graphics-shapes` 的 `RoundedPolygon`。WinUI 3：`CornerRadius` 只有圆弧，`CompositionRoundedRectangleGeometry` 同样。 | 半径数值 1:1 可搬，**曲率形状不可 1:1**。20dp 以上肉眼可辨；建议双端统一用圆弧，并在评审里记一次"已知偏差"。 |
| 2 | **背景模糊**：`BackdropFilter` + `blurSigma 18/24` + `saturation 1.3/1.4`，仅 chrome / sheet 两档启用 | `surfaces.dart`:173-246；`surfaces.dart`:16-19 | Compose：`Modifier.blur(radius)`（API 31+，Android 12 以下无 RenderEffect，只能走 `fallbackFill`）。WinUI：`AcrylicBrush`（模糊强度由系统固定，不可传 sigma）或 `Microsoft.UI.Composition` + Win2D `GaussianBlurEffect`（可控但需自管渲染层）。 | sigma 与饱和度**无法 1:1**。建议：双端都实现 `fallbackFill` 路径为默认可用态，模糊作为可选增强（对应 `blurEnabled` 开关，`surfaces.dart`:144-146）。 |
| 3 | **弹簧物理**：`SpringDescription` + `SpringSimulation` + `settle()`（接手指速度、准确落点） | `app/lib/ui/theme/motion.dart`:62-140 | Compose：`spring(dampingRatio, stiffness)`，无法喂初速与落点修正。WinUI：`CompositionSpringVector3NaturalMotionAnimation`（支持初速，但参数模型不同）。 | 时长可对齐（150/350/500ms 已给出），**手感只能近似**。建议把弹簧全部降级为"时长 + `std` 曲线"，`pressScale 0.97` 等终值保持。 |
| 4 | **居中锚定列表 + 同帧跟随**：`CustomScrollView(center:)`，自定义 `_FollowPhysics` 使新内容在同一帧里贴底 | `app/lib/ui/chat/chat_list.dart`:67-88, 1710-1720 | Compose：`LazyColumn` + `reverseLayout=false` 无法稳定"同帧跟随"；替代为 `LazyColumn(reverseLayout = true)`（新内容在底部、列表从下往上堆）或 `snapshotFlow` + `animateScrollToItem`（会有 1 帧差）。WinUI：`ItemsRepeater` + `ScrollViewer.ChangeView` 同样存在 1 帧差。 | **这是最大还原风险点**。视觉差异表现为"流式时内容先被输入条遮住一帧再跳上来"。建议 M1 直接用 `reverseLayout = true`（语义等价：最新在底部、上翻不打扰），并把"同帧无跳动"列为已知偏差。 |
| 5 | **文字扫光**：`ShaderMask` + `LinearGradient` 在思考行标签上扫亮度 | `thinking_pill.dart`:229-246 | Compose：`TextStyle(brush = ...)` + `drawText`，或用 `ShaderBrush` 的 `translate` 动画（API 31+）。WinUI：需 Composition `LinearGradientBrush` 遮罩 + `TextBlock.Foreground` 动画，成本高。 | 可选增强。降级为静态次级色文字 + 旋转指示器即可，不影响信息传达（`thinking_pill.dart`:237-238 已说明三样同时抢眼就廉价）。 |
| 6 | **思考光球（orb）**：`ThinkingOrb` 自定义 shader，`OrbState.breathing`，20px | `typing_indicator.dart`:5-24；`app/lib/ui/fx/orbs/thinking_orb.dart`（未读） | Compose：无等价物；用三点错峰或旋转圆弧。WinUI：同上。 | **不可一比一**。建议双端统一为"三点错峰"（更通用、更便宜），并在文档里注明与 dsh-Go 不同。 |
| 7 | **滚动回弹**：灵动档 `BouncingScrollPhysics`；默认档交给平台 | `motion.dart`:177-180 | Compose：无回弹，需要自研 `NestedScrollConnection`。WinUI：`ScrollViewer` 自带 overscroll 拉伸指示。 | 低优先级；默认档本身"交给平台"，双端各用自身默认即可。 |
| 8 | **行距分布 `TextLeadingDistribution.even`** | `typography.dart`:116, 170, 189-221 | Compose：`LineHeightStyle(alignment = Center, trim = None)` 近似。WinUI：`LineStackingStrategy = BlockLineHeight` 近似。 | 中文字形上，even 与 center 差异极小；可视为等价。 |
| 9 | **字体**：无衬线走系统（`sansFamily = null`），等宽打包 JetBrains Mono，字重只用 400/600 | `typography.dart`:8-50 | Android：系统即 Noto Sans CJK + JetBrains Mono，可直接复用。Windows：正文用 Segoe UI / MiSans（系统中文档），等宽用 Cascadia Mono；Segoe UI 有 300 字重，但为双端一致仍只用 400/600，展示级（displaySmall 等）用 300。 | 字形不同但层级一致；**字重档位必须双端都压到 400/600**，否则同一份设计在中英文混排时层级会漂。 |
| 10 | **M3 主题手工构造**：`ColorScheme` 每槽手填，`surfaceTint` 透明 | `palette.dart`:11-33, 160-161 | Compose Material3：`lightColorScheme(...)` 可逐槽直搬，`surfaceTint = Color.Transparent`。WinUI 3：**没有 M3**，需自建 `ResourceDictionary` 的 `Color`/`SolidColorBrush`，并显式禁掉任何"给面染主色"的层级效果。 | 颜色数值 100% 可搬；WinUI 需要一次性建令牌层，之后与 Compose 共享同一份加载值（建议从同一份 JSON 生成两端资源）。 |
| 11 | **`Bleed` 点按高亮外溢** | `stream_row.dart`:19, 63-64 | Compose：`Modifier.clickable` 的 ripple 本身可以超出绘制边界（`indication` 用 `bounded = false`）。WinUI：无 ripple，只能用背景色变化，外溢需手动画一个 `Rectangle`。 | 低影响。 |
| 12 | **`AnimatedSwitcher` + 自定义 `layoutBuilder` 的叠层交接**（三点 → 内容） | `assistant_message.dart`:350-378 | Compose：`AnimatedContent`（`transitionSpec` 里同时做透明度与位移）。WinUI：两个 `Grid` 层 + 各自的 `Storyboard`。 | 可实现；关键是"出去的原地淡掉、进来的上移 3px"，这是可抄的行为。 |
| 13 | **`ScrollPhysics` 驱动的"新内容角标"与贴底按钮的显隐 notifier** | `chat_list.dart`:317-322 | 双端都需自己维护 `isPinned` 状态：Compose 用 `derivedStateOf { lastVisibleItemIndex == lastIndex }`；WinUI 用 `ScrollViewer.VerticalOffset + ViewportHeight >= ExtentHeight - ε`。 | 行为可等价。 |
| 14 | **`BoxShadow` 的 blurRadius + offset（无 spread）** | `surfaces.dart`:180-183, 192, 233-236, 245；`notice_row.dart`:240-246 | Compose：`Modifier.shadow(elevation)` 只给环境光/点光源混色，无法分别指定 blur 与 dy；要精确需 `drawBehind` + `Paint.setShadowLayer`（软件层）或画一层渐变/九宫格。WinUI：`ThemeShadow` 或 `DropShadow`（Composition，支持 blur/offset）。 | 阴影强度双端会不一致；建议把"blur/dy/alpha"三值写进令牌，Compose 用 `drawBehind` 自绘以保证与 WinUI 同值。 |

---

## 6 双端实现顺序建议

分层依据：先有令牌层，再有一屏能说话的对话；凡是"没有它就无法完成一次普通对话"的都进 M1。

### M1（普通对话必须，缺一不可）

1. **令牌层**：§1.1–1.9 全部落成两端常量（建议一份 JSON 生成 Compose `Theme` 与 WinUI `ResourceDictionary`）。颜色、字阶、间距、圆角、动效时长先全量落盘，后续组件只准引用令牌，不准写字面量（对应 dsh-Go 的 `AppType` / `AppShape` / `AppSpace` 约束，`typography.dart`:106 明确"业务代码禁止再写 fontFamily 字面量"）。
2. **消息列表骨架**：阅读宽度 720 居中 + 间距 8/24 + 顶部/底部留白（§2.1 Y01–Y10）。
3. **用户气泡**：右对齐、82% 宽、16/8 内边距、lg 圆角（§2.1 Y11–Y22）。
4. **助手正文**：无框全宽、`bodyLarge` 16/1.65、Markdown 段落与代码井底色（§2.1 Y23–Y28；代码井 `codeWellBg` 必须独自铺底，`semantic_colors.dart`:42-44）。
5. **输入栏 + 发送/停止键**：36 键、6 间距、15/1.45、6 行上限、四种键模式（§2.2 全部）。
6. **顶部栏**：56 高、44 胶囊、标题 15/1.3、状态 11/1.25（§2.3 Y50–Y58）。
7. **错误横幅 + 行式错误**：M1 一定会遇到鉴权/配额/网络失败（§3.4 ST27–ST31）。
8. **空态**：首启必见（§3.3 ST16–ST26）。

### M1 之后（有则更好，可后置）

| 顺序 | 组件 | 理由 |
| --- | --- | --- |
| 2 | 流式光标 + 等待指示（§3.1 ST01–ST04） | 信息价值高、成本低，但先保证内容正确再谈光标；建议紧跟 M1。 |
| 3 | 玻璃 chrome 档（`fill` + `hairline` + `rim`，**先不做模糊**） | 只填半透明色即可读作磨砂（`surfaces.dart`:16-19 的结论），模糊作为后续增强。 |
| 4 | 思考折叠行 + 过程行（§3.6、§3.1 ST06–ST09） | 依赖推理模型的思考内容；无思考模型时整行不出现。 |
| 5 | 模型弹层（§2.3 Y59–Y62 + 卡片网格） | 多模型是 BYOK 客户的第二步需求。 |
| 6 | 长按菜单（§3.5） | 复制/引用/重新回答，属打磨项。 |
| 7 | 引用条、回合脚注、状态圆点（§3.2、Y39–Y40、§4） | 依赖多轮与计费数据。 |
| 8 | 弹层拖拽关闭、弹簧动效、扫光、列表逐项错开 | 全部是手感层；在功能闭合后统一做一次。 |
| 9 | 暗色主题 | 令牌已就绪，端上只是切换资源集；可在任一时间点接入。 |

---

## 7 未在源码找到 / 推断清单

| 项 | 状态 | 说明 |
| --- | --- | --- |
| 逐条消息时间戳的位置与样式 | **未在源码找到** | dsh-Go 消息本体不显示时间戳，只有回合脚注（`turn_footnote.dart`:18）。NeoChat 若要逐条时间戳，需自行定义。 |
| 用户/助手头像 | **未在源码找到** | 明确"没有头像也没有署名"（`assistant_message.dart`:36）。 |
| `smoke` / `sheetDark` 的 hex | **未在源码找到（派生值）** | 由 `NeutralTones` 按 accent 从 OKLCH 计算，源注释给出雾蓝 `smoke = OKLCH(0.30, 0.022, 266)`（`surfaces.dart`:214）；`oklch.dart` 换算实现本次未读。 |
| 单行输入条总高 48 | **推断** | 由 `_keySize 36 + _keyGap 6 × 2` 得出；注释写"48 / 圆角 28 = 键半径 20 + 8"与代码 `_keySize = 36` 不一致（`composer.dart`:32-34 vs 174）。以实现算术为准，注释矛盾已记录。 |
| 输入条"上下文环"小件尺寸 | **未在源码找到** | 注释称"发送键左边的小件（上下文环），40dp 高"（`composer.dart`:95），代码内键高为 36，未见该小件自身尺寸定义。 |
| 顶部栏右侧圆钮的容器尺寸 | **未在源码找到** | 只确认图标尺寸 21/22（`chat_header.dart`:210, 319, 341），容器边长未在读取范围内。 |
| 弹层拖拽把手 | **已确认** | 36×4 圆端、`onSurfaceVariant` α0.35、上 10 / 下 12（`app_sheet.dart`:335-341）。 |
| `unknown_results_banner` / `composer_toast` 的视觉细节 | **未读** | 文件存在（分别 282 / 58 行量级），本次未逐行读取，规格未覆盖。 |
| 代码块 / 表格 / 列表的块间距 | **部分** | 确认行内代码圆角 6、padding 4/1（`markdown_body.dart`:177-178）、代码井圆角 10、padding `12,10,14,10`（`markdown_body.dart`:194-196）、表格 cell padding 12/8（`markdown_body.dart`:227）、`hr` 竖向内边距 12（`markdown_body.dart`:165）；段落之间的间距由 Markdown 渲染库配置，**未在源码找到**确定值。 |

---

## 8 出处统计

本次读取的 dsh-Go 文件（均按 `main`、快照 commit `3c8637d2…`）：

| 文件 | 行数 |
| --- | --- |
| `app/lib/ui/theme/palette.dart` | 198 |
| `app/lib/ui/theme/typography.dart` | 223 |
| `app/lib/ui/theme/shapes.dart` | 91 |
| `app/lib/ui/theme/motion.dart` | 229 |
| `app/lib/ui/theme/surfaces.dart` | 315 |
| `app/lib/ui/theme/semantic_colors.dart` | 473 |
| `app/lib/ui/theme/accent.dart` | 459（按 grep 关键段读取） |
| `app/lib/ui/theme/theme_style.dart` | 446（按 grep 读取） |
| `app/lib/ui/chat/widgets/user_bubble.dart` | 350 |
| `app/lib/ui/chat/widgets/assistant_message.dart` | 510 |
| `app/lib/ui/chat/composer.dart` | 1006 |
| `app/lib/ui/chat/chat_list.dart` | 1834（按 grep 读取） |
| `app/lib/ui/chat/chat_header.dart` | 761（按 grep 读取） |
| `app/lib/ui/chat/widgets/message_menu.dart` | 381 |
| `app/lib/ui/chat/widgets/stream_row.dart` | 158 |
| `app/lib/ui/chat/widgets/stream_motion.dart` | 159 |
| `app/lib/ui/chat/widgets/thinking_pill.dart` | 846（按 grep 读取） |
| `app/lib/ui/chat/widgets/notice_row.dart` | 282 |
| `app/lib/ui/chat/widgets/turn_footnote.dart` | 122 |
| `app/lib/ui/chat/widgets/message_actions.dart` | 213 |
| `app/lib/ui/chat/empty_state.dart` | 443（按段读取） |
| `app/lib/ui/chat/empty_actions.dart` | 按段读取（28-65, 125-146） |
| `app/lib/ui/chat/model_sheet.dart` | 1080（按段读取） |
| `app/lib/ui/chat/render/markdown_body.dart` | 888（按 grep 读取） |
| `app/lib/ui/chat/widgets/quote_chip.dart` | 按 grep 读取 |
| `app/lib/ui/chat/widgets/turn_failure_text.dart` | 34 |
| `app/lib/ui/common/typing_indicator.dart` | 25 |
| `app/lib/ui/common/streaming_cursor.dart` | 68 |
| `app/lib/ui/common/status_dot.dart` | 按段读取（1-60） |
| `app/lib/ui/common/status_chip.dart` | 按段读取（1-70） |
| `app/lib/ui/common/capsule_chip.dart` | 53 |
| `app/lib/ui/common/app_sheet.dart` | 按 grep 读取 |
| `app/lib/ui/common/sheet_title_bar.dart` | 按 grep 读取 |

带出处的数值条目：本文件共 **268 条**——§1 令牌 155 条（L01–L15、D01–D15、S01–S11、M01–M18、A01–A08、G01–G18、T01–T25、R01–R07、P01–P09、B01–B05、A-N01–A-N24），§2 布局 63 条（Y01–Y62 含 Y28b），§3 状态与动效 50 条（ST01–ST50）。其中标 **推断** 的 1 条（Y32：单行胶囊高 48 为算术结果，源码注释与代码不一致），§7 另列 **未在源码找到** 7 项、**未读** 1 项、**部分** 1 项。
