namespace NeoChat.Windows.Core.Design;

/// <summary>
/// The single source of every design value in the Windows UI: colours, spacing, radii, type scale,
/// motion durations and layout constants all live here, and nothing else in the UI may carry a literal
/// colour or a magic number. Each member names the ID it comes from in
/// <c>docs/ui/DESIGN-SYSTEM.md</c>; the source line of that ID is kept in the comment so a value can be
/// traced back to dsh-Go without reopening the spec.
/// Values are framework-free (colours as 0xAARRGGBB) so this file can live in the testable core and
/// still be the only place a colour appears; <c>Design/TokenResources.cs</c> in the app turns them into
/// WinUI brushes and structs under <c>TokenKeys</c>-named resource keys.
/// </summary>
public static class Tokens
{
    /// <summary>Colours as 0xAARRGGBB, §1.1 light palette / §1.3 semantic / §1.4 accent / §1.5 glass.</summary>
    public static class Palette
    {
        // §1.1 颜色 · 亮色（晨雾）
        public const uint L01MistTop = 0xFFEBF1FE; // L01 mistTop — palette.dart:37
        public const uint L02MistBottom = 0xFFF6F7FC; // L02 mistBottom — palette.dart:40
        public const uint L03Mist = 0xFFF0F4FD; // L03 mist — palette.dart:43
        public const uint L04MistLowest = 0xFFFBFCFF; // L04 mistLowest — palette.dart:46
        public const uint L05MistLow = 0xFFF6F9FE; // L05 mistLow — palette.dart:47
        public const uint L06MistContainer = 0xFFEEF1F9; // L06 mistContainer — palette.dart:48
        public const uint L07MistHigh = 0xFFE7EBF4; // L07 mistHigh — palette.dart:49
        public const uint L08MistHighest = 0xFFDFE4EE; // L08 mistHighest — palette.dart:50
        public const uint L09Ink = 0xFF1E232E; // L09 ink — palette.dart:53
        public const uint L10InkMuted = 0xFF4F5562; // L10 inkMuted — palette.dart:56
        public const uint L11InkFaint = 0xFF646974; // L11 inkFaint — palette.dart:59
        public const uint L12InkGhost = 0xFFA1A5AC; // L12 inkGhost — palette.dart:62
        public const uint L13OutlineLight = 0xFF7E838D; // L13 outlineLight — palette.dart:65
        public const uint L14RuleLight = 0xFFDCE0E8; // L14 ruleLight — palette.dart:68
        public const uint L15ShadowInk = 0xFF0B152D; // L15 shadowInk — palette.dart:71

        // §1.3 语义色（亮色列）
        public const uint M01TextTertiary = 0xFF646974; // M01 textTertiary — semantic_colors.dart:183
        public const uint M02TextDisabled = 0xFFA1A5AC; // M02 textDisabled — semantic_colors.dart:184
        public const uint M03Success = 0xFF337250; // M03 success — semantic_colors.dart:186
        public const uint M05Warning = 0xFF936123; // M05 warning — semantic_colors.dart:190
        public const uint S01Slate = 0xFF545E6F; // S01 slate — palette.dart:94
        public const uint S02SlateContainer = 0xFFE2E6F0; // S02 slateContainer — palette.dart:95
        public const uint S07Danger = 0xFFA54743; // S07 danger = M07 — palette.dart:112
        public const uint S08OnDanger = 0xFFFDF9F8; // S08 onDanger — palette.dart:113
        public const uint S09DangerContainer = 0xFFFFE2E0; // S09 dangerContainer = M08 — palette.dart:114
        public const uint S10OnDangerContainer = 0xFF642724; // S10 onDangerContainer — palette.dart:115
        public const uint M09Info = 0xFF2971A7; // M09 info — semantic_colors.dart:198

        // §1.4 强调色（mist 默认套）
        public const uint A01Seed = 0xFF4B67B4; // A01 seed（primary）— accent.dart:128
        public const uint A02Pressed = 0xFF3D569D; // A02 pressed — accent.dart:129
        public const uint A03Container = 0xFFDEE8FF; // A03 container — accent.dart:130
        public const uint A04OnContainer = 0xFF213262; // A04 onContainer — accent.dart:131
        public const uint A05OnSeed = 0xFFFFFFFF; // A05 onSeed — accent.dart:118
        public const uint A06Bubble = 0xFFD9E4FD; // A06 bubble（用户气泡底，= G17）— accent.dart:132
        public const uint A07Glow = 0xFFABC3FC; // A07 glow — accent.dart:133
        public const uint A08Haze = 0xFFD5CBEB; // A08 haze — accent.dart:143

        // §1.5 玻璃材质（chrome / card / pill；模糊关闭，走 fallbackFill，见 G18）
        public const uint G01ChromeFill = 0xB3FFFFFF; // G01 chrome fill = 白 70% — surfaces.dart:174
        public const uint G02ChromeFallbackFill = 0xF5FBFCFF; // G02 chrome fallbackFill = mistLowest 96% — surfaces.dart:175
        public const uint G03ChromeHairline = 0x140B152D; // G03 chrome hairline = shadowInk 8% — surfaces.dart:176
        public const uint G04ChromeRim = 0xD9FFFFFF; // G04 chrome rim = 白 85% — surfaces.dart:177
        public const uint G11CardFill = 0x9EFFFFFF; // G11 card fill = 白 62% — surfaces.dart:195
        public const uint G12CardHairline = 0x0F0B152D; // G12 card hairline = 6% — surfaces.dart:197
        public const uint G12CardRim = 0xB3FFFFFF; // G12 card rim = 白 70% — surfaces.dart:198
        public const uint G13PillFill = 0x8CFFFFFF; // G13 pill fill = 白 55% — surfaces.dart:201
        public const uint G14PillHairline = 0x120B152D; // G14 pill hairline = 7% — surfaces.dart:203
        public const uint G16Scrim = 0x520B111F; // G16 scrim = #0B111F 32% — surfaces.dart:170

        // §1.3 语义色 · 代码井（M13–M17；Markdown 渲染新增，此前未被 UI 使用）
        public const uint M13CodeWellBg = 0xFFEDF0F6; // M13 codeWellBg（代码井底）— semantic_colors.dart:206
        public const uint M14CodeWellHeader = 0xFFE7EBF3; // M14 codeWellHeader — semantic_colors.dart:207
        public const uint M15CodeWellFg = 0xFF1E232E; // M15 codeWellFg — semantic_colors.dart:208
        public const uint M16CodeWellBorder = 0xFFDBDEE5; // M16 codeWellBorder — semantic_colors.dart:209
        public const uint M17LineNumberFg = 0xFF7D8088; // M17 lineNumberFg — semantic_colors.dart:210

        /// <summary>Non-design helper: transparency used by control templates that must not paint.</summary>
        public const uint Transparent = 0x00000000; // 非设计数值：透明，供控件模板关闭默认底色
    }

    /// <summary>Motion durations, §1.10.</summary>
    public static class Motion
    {
        public static readonly TimeSpan Press = TimeSpan.FromMilliseconds(90); // A-N01 press — motion.dart:20
        public static readonly TimeSpan Quick = TimeSpan.FromMilliseconds(160); // A-N02 quick — motion.dart:23
        public static readonly TimeSpan Standard = TimeSpan.FromMilliseconds(240); // A-N03 standard — motion.dart:26
        public static readonly TimeSpan Collapse = TimeSpan.FromMilliseconds(280); // A-N04 collapse — motion.dart:29
        public static readonly TimeSpan Emphasized = TimeSpan.FromMilliseconds(340); // A-N05 emphasized — motion.dart:32
        public static readonly TimeSpan GlassIn = TimeSpan.FromMilliseconds(120); // A-N07 glassIn — motion.dart:39
        public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(480); // A-N08 slow — motion.dart:42
        public const double BreathSeconds = 2.4; // A-N09 breath 2400ms — motion.dart:45
        public const double PressScale = 0.97; // A-N13 pressScale — motion.dart:162

        /// <summary>ST01: cursor opacity cycle is 1000 ms and breathing spans 1.0 ↔ 0.25.</summary>
        public const double CursorCycleSeconds = 1.0; // ST01 — streaming_cursor.dart:9-23
        public const double CursorOpacityHigh = 1.0; // ST01 — streaming_cursor.dart:29-66
        public const double CursorOpacityLow = 0.25; // ST01 — streaming_cursor.dart:29-66

        /// <summary>ST12: status dot breathes 1 ↔ 0.35 over 1200 ms.</summary>
        public const double StatusDotCycleSeconds = 1.2; // ST12 — status_dot.dart:28-46
        public const double StatusDotOpacityHigh = 1.0; // ST12 — status_dot.dart:28-46
        public const double StatusDotOpacityLow = 0.35; // ST12 — status_dot.dart:28-46
    }

    /// <summary>Spacing ladder, §1.8.</summary>
    public static class Space
    {
        public const double Xxs = 2; // P01 xxs — shapes.dart:63
        public const double Xs = 4; // P01 xs — shapes.dart:64
        public const double Sm = 8; // P01 sm — shapes.dart:65
        public const double Md = 12; // P02 md — shapes.dart:66
        public const double Lg = 16; // P02 lg — shapes.dart:67
        public const double Xl = 20; // P02 xl — shapes.dart:68
        public const double Xxl = 24; // P03 xxl — shapes.dart:69
        public const double Xxxl = 32; // P03 xxxl — shapes.dart:70
        public const double Gutter = 16; // P04 gutter — shapes.dart:73
        public const double InTurn = 8; // P05 inTurn — shapes.dart:76
        public const double BetweenTurns = 24; // P06 betweenTurns — shapes.dart:79
        public const double ReadingWidth = 720; // P07 / Y01 readingWidth — shapes.dart:83
        public const double FloatInset = 12; // P08 floatInset — shapes.dart:87
        public const double FloatingWidth = 696; // P09 floatingWidth — shapes.dart:90
    }

    /// <summary>Corner radii, §1.7 (continuous curvature is not available in WinUI; values only).</summary>
    public static class Radius
    {
        public const double Xs = 6; // R01 xs — shapes.dart:25
        public const double Sm = 10; // R02 sm — shapes.dart:26
        public const double Md = 14; // R03 md — shapes.dart:27
        public const double Lg = 20; // R04 lg（用户气泡）— shapes.dart:28
        public const double Xl = 28; // R05 xl（输入条）— shapes.dart:29
        public const double Capsule = 22; // R06 pill：顶栏胶囊高 44 / 2 — shapes.dart:50, chat_header.dart:91
        public const double CursorBar = 1.5; // ST01 光标圆角 — streaming_cursor.dart:9-23
    }

    /// <summary>Border widths, §1.9.</summary>
    public static class Border
    {
        public const double Hairline = 0.5; // B01 发丝描边 — surfaces.dart:33
        public const double Card = 1; // B02 卡片描边 — shapes.dart:53-55
        public const double ErrorBanner = 0.5; // B03 错误横幅描边宽 — notice_row.dart:239
        public const double ErrorBannerAlpha = 0.3; // B03 描边色 = accent α0.3 — notice_row.dart:239
        public const double ErrorBannerFillAlpha = 0.92; // ST27 底 = dangerContainer α0.92 — notice_row.dart:209-282
    }

    /// <summary>Type scale, §1.6. Line heights are the ratio applied to the size.</summary>
    public static class Type
    {
        public const double DisplaySmallSize = 28; // T03 displaySmall（欢迎语）— typography.dart:191
        public const double DisplaySmallLineHeight = 35; // T03 行高 1.25 — typography.dart:191
        public const double HeadlineMediumSize = 22; // T05 headlineMedium — typography.dart:193
        public const double HeadlineMediumLineHeight = 28.6; // T05 行高 1.3 — typography.dart:193
        public const double TitleLargeSize = 18; // T07 titleLarge — typography.dart:195
        public const double TitleLargeLineHeight = 25.2; // T07 行高 1.4 — typography.dart:195
        public const double TitleSmallSize = 15; // Y54 会话名覆盖为 15 — chat_header.dart:158
        public const double TitleSmallLineHeight = 19.5; // Y54 行高 1.3 — chat_header.dart:158
        public const double BodyLargeSize = 16; // T10 bodyLarge（聊天正文）— typography.dart:198
        public const double BodyLargeLineHeight = 26.4; // T10 行高 1.65 — typography.dart:198
        public const double BodyMediumSize = 14; // T11 bodyMedium — typography.dart:199
        public const double BodyMediumLineHeight = 21.7; // T11 行高 1.55 — typography.dart:199
        public const double BodySmallSize = 12; // T12 bodySmall — typography.dart:200
        public const double BodySmallLineHeight = 18; // T12 行高 1.5 — typography.dart:200
        public const double LabelLargeSize = 14; // T13 labelLarge（按钮）— typography.dart:201-207
        public const double LabelLargeLineHeight = 19.6; // T13 行高 1.4 — typography.dart:201-207
        public const double LabelMediumSize = 12; // T14 labelMedium — typography.dart:208-214
        public const double LabelMediumLineHeight = 16.2; // T14 行高 1.35 — typography.dart:208-214
        public const double LabelSmallSize = 11; // T15 labelSmall（时间戳/角标）— typography.dart:215-221
        public const double LabelSmallLineHeight = 14.85; // T15 行高 1.35 — typography.dart:215-221
        public const double HeaderStatusLineHeight = 13.75; // Y55 状态文字覆盖为 11 / 1.25 — chat_header.dart:159
        public const double ModelChipSize = 13; // Y61 模型胶囊文字覆盖为 13 — model_sheet.dart:151
        public const double ModelChipLineHeight = 17.55; // Y61 labelMedium 1.35 — model_sheet.dart:151
        public const double ComposerSize = 15; // Y34 输入字号 15 — composer.dart:178
        public const double ComposerLineHeight = 21.75; // Y34 输入行高 1.45 — composer.dart:178, 28
        public const double StreamRowLabelLineHeight = 18.2; // ST50 行标签 1.3 × bodyMedium — stream_row.dart:35-43
        public const double StreamRowDetailSize = 13; // ST50 行内细节 13 — stream_row.dart:35-43
        public const double StreamRowDetailLineHeight = 16.9; // ST50 行内细节 1.3 — stream_row.dart:35-43

        public const string SansFamily = "Segoe UI"; // §5 差异 9：WinUI 正文走系统字
        public const string IconFamily = "Segoe Fluent Icons"; // 任务单：图标用内置矢量
        public const string MonoFamily = "Cascadia Mono"; // §5 差异 9：Windows 等宽用 Cascadia Mono

        // Markdown 渲染新增（M1-WIN-003）：等宽字阶与标题字阶，全部取自既有条目
        public const double MonoSize = 13; // T16 等宽正文 mono 13 — typography.dart:107-118
        public const double MonoLineHeight = 20.15; // T16 行高 1.55 × 13 — typography.dart:107-118
        public const double MonoSmallSize = 12; // T17 等宽小号 monoSmall 12 — typography.dart:121
        public const double MonoSmallLineHeight = 18; // T17 行高 1.5 × 12 — typography.dart:121
        public const double MonoLabelSize = 12; // T18 等宽标签 monoLabel 12 — typography.dart:124-125
        public const double MonoLabelLineHeight = 16.2; // T18 行高 1.35 × 12 — typography.dart:124-125

        public const double Heading1Size = 26; // T04 headlineLarge 26 — typography.dart:192
        public const double Heading1LineHeight = 33.8; // T04 行高 1.3 × 26 — typography.dart:192
        public const double Heading2Size = 22; // T05 headlineMedium 22 — typography.dart:193
        public const double Heading2LineHeight = 28.6; // T05 行高 1.3 × 22 — typography.dart:193
        public const double Heading3Size = 20; // T06 headlineSmall 20 — typography.dart:194
        public const double Heading3LineHeight = 27; // T06 行高 1.35 × 20 — typography.dart:194
        public const double Heading4Size = 18; // T07 titleLarge 18 — typography.dart:195
        public const double Heading4LineHeight = 25.2; // T07 行高 1.4 × 18 — typography.dart:195
        public const double Heading5Size = 16; // T08 titleMedium 16 — typography.dart:196
        public const double Heading5LineHeight = 23.2; // T08 行高 1.45 × 16 — typography.dart:196
        public const double Heading6Size = 14; // T09 titleSmall 14 — typography.dart:197
        public const double Heading6LineHeight = 20.3; // T09 行高 1.45 × 14 — typography.dart:197
    }

    /// <summary>
    /// Layout constants with a fixed design ID. Values that the spec derives arithmetically are marked
    /// with the arithmetic in the comment instead of a single source line.
    /// </summary>
    public static class Layout
    {
        public const double HeaderHeight = 56; // Y50 barHeight — chat_header.dart:89
        public const double HeaderCapsuleMinHeight = 44; // Y51 标题胶囊高 — chat_header.dart:91-98
        public const double HeaderPadding = 12; // Y53 水平内边距 — chat_header.dart:186
        public const double HeaderKeySize = 36; // 未在源码找到（容器边长）；取 Y30 键尺寸 36 — composer.dart:174
        public const double HeaderIconSize = 22; // Y57 左右圆钮图标 22 — chat_header.dart:210, 341
        public const double StatusDotSize = 6; // Y56 状态圆点直径 — chat_header.dart:362-363
        public const double StatusDotGap = 5; // Y56 圆点与文字间距 — chat_header.dart:362-363
        public const double ModelCapsuleMinHeight = 44; // Y59 模型胶囊最小高 — model_sheet.dart:163-164
        public const double ModelCapsulePaddingStart = 12; // Y60 左内边距 — model_sheet.dart:133-138
        public const double ModelCapsulePaddingEnd = 8; // Y60 右内边距 — model_sheet.dart:133-138
        public const double ModelChevronSize = 16; // Y60 chevron 16 — model_sheet.dart:133-138

        public const double ListTopInset = 8; // Y02 顶部留白 topInset + 8 — chat_list.dart:1014
        public const double ListBottomInset = 8; // Y03 底部留白 bottomInset + 8 — chat_list.dart:973
        public const double FollowThreshold = 8; // Y10 贴底判定的最小空档 sm — chat_list.dart:176
        public const double BubbleMaxWidthRatio = 0.82; // Y11 用户气泡最大宽度 82% — user_bubble.dart:174, 190
        public const double BubblePaddingHorizontal = 16; // Y12 内边距水平 — user_bubble.dart:166
        public const double BubblePaddingVertical = 8; // Y12 内边距垂直 — user_bubble.dart:166
        public const double JumpToBottomInset = 12; // Y09 回到底部按钮距底 — chat_list.dart:1053-1054

        public const double StreamLead = 24; // ST45 图形列宽 — stream_row.dart:21, 72-80
        public const double StreamGap = 8; // ST46 图形与文字间距 — stream_row.dart:24
        public const double StreamIndent = 32; // ST47 文字列起点 — stream_row.dart:27
        public const double StreamRowMinHeight = 32; // ST48 单行最小高 — stream_row.dart:30, 57-59
        public const double ThinkingWindowLines = 3; // ST08 思考实时窗口最多三行 — thinking_pill.dart:37-42
        public const double CursorWidth = 2.5; // ST01 光标宽 — streaming_cursor.dart:9-23
        public const double CursorHeight = 18; // ST01 光标高 — streaming_cursor.dart:9-23
        public const double InterruptedIconSize = 16; // Y26 中断标记图标 16 — assistant_message.dart:217-219

        public const double ComposerKeySize = 36; // Y30 键尺寸 — composer.dart:174
        public const double ComposerKeyGap = 6; // Y31 键到胶囊边 — composer.dart:175
        public const double ComposerHeight = 48; // Y32 单行胶囊总高 = 36 + 6 + 6 — composer.dart:174-175, 602
        public const double ComposerPaddingStart = 18; // Y33 内边距 left 18 — composer.dart:602
        public const double ComposerPadding = 6; // Y33 内边距 6 — composer.dart:602
        public const double ComposerMaxLines = 6; // Y36 最大可见行数 — composer.dart:66
        public const double ComposerMaxHeight = 130.5; // Y36 × Y34 = 6 × 15 × 1.45 — composer.dart:66, 178
        public const double ComposerIconSize = 20; // Y45 send 图标 — composer.dart:928-931
        public const double ComposerStopIconSize = 18; // Y45 stop 图标 — composer.dart:928-931
        public const double ComposerPressScale = 0.9; // Y46 按下缩放 — composer.dart:938-940
        public const double ComposerFooterGap = 2; // Y42 文字与下一行间距 — composer.dart:732-742

        public const double ErrorIconSize = 17; // ST27 图标 17 — notice_row.dart:209-282
        public const double ErrorIconGap = 8; // ST27 图标与文字间距 — notice_row.dart:209-282
        public const double ErrorMaxTextLines = 3; // ST27 文字最多 3 行 — notice_row.dart:209-282
        public const double ErrorActionMinSize = 48; // ST27 操作按钮最小 48 × 48 — notice_row.dart:209-282
        public const double ErrorCloseSize = 40; // ST27 关闭按钮 40 × 40 — notice_row.dart:209-282
        public const double ErrorCloseIconSize = 18; // ST27 关闭图标 18 — notice_row.dart:209-282

        public const double EmptyMargin = 12; // ST16 左右边距 — empty_state.dart:202-203
        public const double EmptyMaxWidth = 696; // ST16 最大宽 — empty_state.dart:202-203
        public const double EmptySubtitleGap = 4; // ST18 副标题间距 — empty_state.dart:317-326
        public const double EmptyCapsuleGap = 12; // ST19 副标题 → 胶囊 — empty_state.dart:206
        public const double EmptyOptionsGap = 8; // ST20 胶囊 → 选项行 — empty_state.dart:209
        public const double EmptySuggestionsGap = 28; // ST21 选项行 → 建议 — empty_state.dart:212
        public const double EmptyHintGap = 6; // ST22 建议 → 提示行 — empty_state.dart:215
        public const double EmptyCapsuleHeight = 36; // ST23 主胶囊高 36 — empty_actions.dart:30-36
        public const double EmptyCapsulePadStart = 18; // ST23 水平 padding 14 + 4 — empty_actions.dart:30-36, 138-143
        public const double EmptyCapsulePadEnd = 14; // ST23 水平 padding 14 — empty_actions.dart:138-143
        public const double EmptyCapsulePadVertical = 6; // ST23 垂直 padding 6 — empty_actions.dart:138-143
        public const double EmptyChipHeight = 36; // ST24 安静 chip 高 36 — empty_actions.dart:38-48
        public const double EmptyChipGap = 8; // ST25 建议 chip 间距 — empty_actions.dart:51
        public const double EmptyChipPadding = 8; // ST26 chip 内边距 — empty_state.dart:383-386

        public const double GlowPrimaryAlpha = 0.42; // G15 glowPrimary α — surfaces.dart:166-167
        public const double GlowSecondaryAlpha = 0.38; // G15 glowSecondary α — surfaces.dart:166-167
        public const double GlowDiameter = 520; // 非设计数值：光晕尺寸，源码未给出（surfaces.dart 只给颜色与 α）
        public const double OptimisticBubbleOpacity = 0.72; // Y18 乐观态透明度 — user_bubble.dart:185
        public const double ComposerTextVerticalPadding = 7.125; // Y35 max(4, (36 − 15 × 1.45) / 2) — composer.dart:388-389
        public const double IconGlyphSize = 16; // Y26 / ST24 行内图标 16 — assistant_message.dart:217-219, empty_actions.dart:38-48
        public const double SendDisabledAlpha = 0.12; // Y43 发送键禁用底 = onSurface 12% — composer.dart:903-905
        public const double SendForegroundDisabledAlpha = 0.38; // Y44 发送键禁用前景 = onSurface 38% — composer.dart:905-906
        public const double SendDisabledForegroundSize = 18; // Y45 stop 图标 18 — composer.dart:928-931

        // Markdown 渲染新增（M1-WIN-003）。DESIGN-SYSTEM §7「未在源码找到/部分」一栏给出的实现值：
        // 行内代码圆角 6、padding 4/1（markdown_body.dart:177-178）；代码井圆角 10、padding 12,10,14,10
        // （markdown_body.dart:194-196）；表格 cell padding 12/8（markdown_body.dart:227）；hr 竖向内边距 12
        // （markdown_body.dart:165）。设计系统 §1.7 把「代码井」列在 R03 md=14 下，与源码行 194-196 的 10 冲突，
        // 以源码实现值为准，冲突已写入交付的待决问题段。
        public const double CodeWellRadius = 10; // §7 代码井圆角 10 — markdown_body.dart:194-196（与 R03 md=14 冲突）
        public const double CodeWellPaddingStart = 12; // §7 代码井内边距左 12 — markdown_body.dart:194-196
        public const double CodeWellPaddingTop = 10; // §7 代码井内边距上 10 — markdown_body.dart:194-196
        public const double CodeWellPaddingEnd = 14; // §7 代码井内边距右 14 — markdown_body.dart:194-196
        public const double CodeWellPaddingBottom = 10; // §7 代码井内边距下 10 — markdown_body.dart:194-196
        public const double InlineCodeRadius = 6; // R01 xs 行内代码圆角 6 — shapes.dart:25, markdown_body.dart:177-178
        public const double InlineCodePaddingHorizontal = 4; // §7 行内代码 padding 水平 4 — markdown_body.dart:177-178
        public const double InlineCodePaddingVertical = 1; // §7 行内代码 padding 垂直 1 — markdown_body.dart:177-178
        public const double TableCellPaddingHorizontal = 12; // §7 表格 cell padding 水平 12 — markdown_body.dart:227
        public const double TableCellPaddingVertical = 8; // §7 表格 cell padding 垂直 8 — markdown_body.dart:227
        public const double HrMarginVertical = 12; // §7 hr 竖向内边距 12 — markdown_body.dart:165
        public const double MarkdownBlockGap = 8; // P05 inTurn 同轮内块间距 8 — shapes.dart:76
        public const double ListIndent = 20; // P02 xl 列表每级缩进 20 — shapes.dart:68（设计系统未给列表缩进，取既有档位）
        public const double ListMarkerWidth = 18; // P02 md 12 + P01 xs 4 的标记列宽（设计系统未给列表标记列，推断值）
        public const double BlockQuoteBarWidth = 3; // 推断值：设计系统无引用条令牌，取 3 以对齐 B01/B02 之间的视觉重量
        public const double TableMinColumnWidth = 96; // 推断值：设计系统无表格最小列宽，防单字列挤压
        public const double MathBadgeSize = 11; // T15 labelSmall 11（公式降级标注）— typography.dart:215-221
    }
}
