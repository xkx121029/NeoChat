package com.neochat.app.ui.theme

import androidx.compose.animation.core.CubicBezierEasing
import androidx.compose.animation.core.Easing
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.TextUnit
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/**
 * 唯一的令牌来源。每个数值都标注它在 `docs/ui/DESIGN-SYSTEM.md` 里的条目 ID。
 * 界面代码只准引用这里的常量：颜色、间距、圆角、字号、动效时长一律不写字面量。
 *
 * 只落盘 M1（普通对话）实际用到的条目；未落盘的条目在交付报告的偏差清单里逐条写明，不补近似值。
 */

/** §1.1 颜色 · 亮色（晨雾）。 */
internal object LightInk {
    /** L01 `mistTop` 页面渐变顶。 */
    val mistTop: Color = Color(0xFFEBF1FE)

    /** L02 `mistBottom` 页面渐变底。 */
    val mistBottom: Color = Color(0xFFF6F7FC)

    /** L03 `mist` 不透明页面底。 */
    val mist: Color = Color(0xFFF0F4FD)

    /** L04 `mistLowest`。 */
    val mistLowest: Color = Color(0xFFFBFCFF)

    /** L09 `ink` 主文字。 */
    val ink: Color = Color(0xFF1E232E)

    /** L10 `inkMuted` 次级文字。 */
    val inkMuted: Color = Color(0xFF4F5562)

    /** L11 `inkFaint` 三级文字。 */
    val inkFaint: Color = Color(0xFF646974)

    /** L13 `outlineLight` 交互边界。 */
    val outlineLight: Color = Color(0xFF7E838D)

    /** L14 `ruleLight` 分割线/发丝描边。 */
    val ruleLight: Color = Color(0xFFDCE0E8)
}

/** §1.3 中性辅色 / 语义色 · 亮色。 */
internal object LightSemantic {
    /** S01 `slate`（secondary）。 */
    val slate: Color = Color(0xFF545E6F)

    /** S02 `slateContainer`（secondaryContainer）。 */
    val slateContainer: Color = Color(0xFFE2E6F0)

    /** S04 `dusk`（tertiary）。 */
    val dusk: Color = Color(0xFF6B578F)

    /** S05 `duskContainer`。 */
    val duskContainer: Color = Color(0xFFECE5FB)

    /** S06 `onDuskContainer`。 */
    val onDuskContainer: Color = Color(0xFF3B2C54)

    /** S07 `danger`。 */
    val danger: Color = Color(0xFFA54743)

    /** S08 `onDanger`。 */
    val onDanger: Color = Color(0xFFFDF9F8)

    /** S09 `dangerContainer`。 */
    val dangerContainer: Color = Color(0xFFFFE2E0)

    /** S10 `onDangerContainer`。 */
    val onDangerContainer: Color = Color(0xFF642724)

    /** S11 `scrim`。 */
    val scrim: Color = Color(0xFF0B111F)

    /** M01 `textTertiary`。 */
    val textTertiary: Color = Color(0xFF646974)

    /** M02 `textDisabled`。 */
    val textDisabled: Color = Color(0xFFA1A5AC)

    /** M05 `warning`。 */
    val warning: Color = Color(0xFF936123)
}

/** §1.4 强调色（默认配色 `mist`）· 亮色。 */
internal object LightAccent {
    /** A01 `seed`（primary）。 */
    val seed: Color = Color(0xFF4B67B4)

    /** A02 `pressed`。 */
    val pressed: Color = Color(0xFF3D569D)

    /** A03 `container`。 */
    val container: Color = Color(0xFFDEE8FF)

    /** A04 `onContainer`。 */
    val onContainer: Color = Color(0xFF213262)

    /** A05 `onSeed`。 */
    val onSeed: Color = Color(0xFFFFFFFF)

    /** A06 `bubble` 用户气泡底。 */
    val bubble: Color = Color(0xFFD9E4FD)
}

/** §1.5 玻璃材质 · 亮色（只做 `fill` + `hairline`，不做模糊，见 §6 M1 之后第 3 条）。 */
internal object LightGlass {
    /** G01 chrome `fill`：白 70%。 */
    val chromeFill: Color = Color(0xB3FFFFFF)

    /** G03 chrome `hairline`：`shadowInk` 8%。 */
    val chromeHairline: Color = Color(0x140B152D)

    /** G13 pill `fill`：白 55%。 */
    val pillFill: Color = Color(0x8CFFFFFF)

    /** G14 pill `hairline`：7%。 */
    val pillHairline: Color = Color(0x120B152D)
}

/** 页面里实际使用的颜色角色；每个字段的注释给出它对应的条目 ID。 */
public data class NeoChatColors(
    /** L01。 */
    public val pageTop: Color,
    /** L02。 */
    public val pageBottom: Color,
    /** L03。 */
    public val pageFallback: Color,
    /** L04。 */
    public val raisedSurface: Color,
    /** L09。 */
    public val ink: Color,
    /** L10。 */
    public val inkMuted: Color,
    /** L11 + M01。 */
    public val inkFaint: Color,
    /** L13。 */
    public val outline: Color,
    /** L14。 */
    public val rule: Color,
    /** A01。 */
    public val accent: Color,
    /** A02。 */
    public val accentPressed: Color,
    /** A03。 */
    public val accentContainer: Color,
    /** A04。 */
    public val onAccentContainer: Color,
    /** A05。 */
    public val onAccent: Color,
    /** A06 + G17。 */
    public val userBubble: Color,
    /** G17（亮色取 `ink`）。 */
    public val onUserBubble: Color,
    /** S01。 */
    public val slate: Color,
    /** S02。 */
    public val slateContainer: Color,
    /** S04。 */
    public val dusk: Color,
    /** S05。 */
    public val duskContainer: Color,
    /** S06。 */
    public val onDuskContainer: Color,
    /** S07 + M07。 */
    public val danger: Color,
    /** S08 + M07。 */
    public val onDanger: Color,
    /** S09 + M08。 */
    public val dangerContainer: Color,
    /** S10 + M08。 */
    public val onDangerContainer: Color,
    /** M05。 */
    public val warning: Color,
    /** M01（空态提示行）。 */
    public val textTertiary: Color,
    /** S11（对话框遮罩）。 */
    public val scrim: Color,
    /** G01。 */
    public val chromeFill: Color,
    /** G03。 */
    public val chromeHairline: Color,
    /** G13。 */
    public val pillFill: Color,
    /** G14。 */
    public val pillHairline: Color,
    /** ST01 光标色（主色）。 */
    public val streamingCursor: Color,
)

/** 亮色（`mist`）实例。 */
public val NeoChatLightColors: NeoChatColors = NeoChatColors(
    pageTop = LightInk.mistTop,
    pageBottom = LightInk.mistBottom,
    pageFallback = LightInk.mist,
    raisedSurface = LightInk.mistLowest,
    ink = LightInk.ink,
    inkMuted = LightInk.inkMuted,
    inkFaint = LightSemantic.textTertiary,
    outline = LightInk.outlineLight,
    rule = LightInk.ruleLight,
    accent = LightAccent.seed,
    accentPressed = LightAccent.pressed,
    accentContainer = LightAccent.container,
    onAccentContainer = LightAccent.onContainer,
    onAccent = LightAccent.onSeed,
    userBubble = LightAccent.bubble,
    onUserBubble = LightInk.ink,
    slate = LightSemantic.slate,
    slateContainer = LightSemantic.slateContainer,
    dusk = LightSemantic.dusk,
    duskContainer = LightSemantic.duskContainer,
    onDuskContainer = LightSemantic.onDuskContainer,
    danger = LightSemantic.danger,
    onDanger = LightSemantic.onDanger,
    dangerContainer = LightSemantic.dangerContainer,
    onDangerContainer = LightSemantic.onDangerContainer,
    warning = LightSemantic.warning,
    textTertiary = LightSemantic.textTertiary,
    scrim = LightSemantic.scrim,
    chromeFill = LightGlass.chromeFill,
    chromeHairline = LightGlass.chromeHairline,
    pillFill = LightGlass.pillFill,
    pillHairline = LightGlass.pillHairline,
    streamingCursor = LightAccent.seed,
)

/** §1.8 间距阶梯。 */
public object Space {
    /** 零值：只用于"无间距/贴边"这种结构判断，不是设计数值。 */
    public val none: Dp = 0.dp

    /** P01 `xxs`。 */
    public val xxs: Dp = 2.dp

    /** P01 `xs`。 */
    public val xs: Dp = 4.dp

    /** P01 `sm`。 */
    public val sm: Dp = 8.dp

    /** P02 `md`。 */
    public val md: Dp = 12.dp

    /** P02 `lg`。 */
    public val lg: Dp = 16.dp

    /** P02 `xl`。 */
    public val xl: Dp = 20.dp

    /** P03 `xxl`。 */
    public val xxl: Dp = 24.dp

    /** P04 `gutter` 页面水平留白。 */
    public val gutter: Dp = 16.dp

    /** P07 `readingWidth` 正文最大阅读宽度。 */
    public val readingWidth: Dp = 720.dp

    /** P09 `floatingWidth` = `readingWidth − 2 × floatInset`。 */
    public val floatingWidth: Dp = 696.dp

    /** Y02 顶部留白 = `topInset + 8`（`topInset` 由系统栏 inset 承担）。 */
    public val listTopInset: Dp = 8.dp

    /** Y03 底部留白 = `bottomInset + 8 + headroom`（`bottomInset` 由输入条自身高度承担）。 */
    public val listBottomInset: Dp = 8.dp

    /** Y05 用户消息→用户消息。 */
    public val userToUser: Dp = 8.dp

    /** Y06 用户消息→上一条非用户。 */
    public val userToNonUser: Dp = 24.dp

    /** Y10 贴底判定的最小空档。 */
    public val pinnedSlack: Dp = 8.dp
}

/** §1.7 圆角。 */
public object Radii {
    /** R03 `md`（卡片、输入框、主按钮）。 */
    public val md: Dp = 14.dp

    /** R04 `lg`（用户气泡）。 */
    public val lg: Dp = 20.dp

    /** R05 `xl`（输入条、对话框）。 */
    public val xl: Dp = 28.dp
}

/** §1.9 边框宽度。 */
public object Stroke {
    /** B01 发丝描边默认宽。 */
    public val hairline: Dp = 0.5.dp

    /** B03 错误横幅描边。 */
    public val errorBanner: Dp = 0.5.dp
}

/** §1.10 动效时长与曲线。 */
public object Motion {
    /** A-N01 `press`。 */
    public const val pressMs: Int = 90

    /** A-N03 `standard`。 */
    public const val standardMs: Int = 240

    /** A-N04 `collapse`。 */
    public const val collapseMs: Int = 280

    /** A-N11 `stagger`。 */
    public const val staggerMs: Int = 30

    /** ST01 光标呼吸周期。 */
    public const val cursorBlinkMs: Int = 1000

    /** ST12 状态圆点呼吸周期。 */
    public const val statusDotBreathMs: Int = 1200

    /** Y46 发送键按下缩放 0.9。 */
    public const val sendKeyPressScale: Float = 0.9f

    /** A-N14 `std` ≈ cubic-bezier(.4, 0, .2, 1)。 */
    public val std: Easing = CubicBezierEasing(0.4f, 0f, 0.2f, 1f)

    /** A-N16 `collapseCurve` = easeInOutCubic 近似。 */
    public val collapseCurve: Easing = CubicBezierEasing(0.65f, 0f, 0.35f, 1f)
}

/** §1.6 一个字号档位：字号 sp、字重、行高倍数、字距 sp。 */
public data class TypeToken(
    public val sizeSp: Int,
    public val weight: FontWeight,
    public val lineHeightMultiplier: Float,
    public val letterSpacingSp: Float = 0f,
) {
    /** 字号。 */
    public val size: TextUnit
        get() = sizeSp.sp

    /** 行高绝对值。 */
    public val lineHeight: TextUnit
        get() = (sizeSp * lineHeightMultiplier).sp

    /** 字距。 */
    public val letterSpacing: TextUnit
        get() = letterSpacingSp.sp
}

/** §1.6 字号 / 字重 / 行高。 */
public object TypeScale {
    /** T01 `displayLarge` 36 / w300 / 1.2。 */
    public val displayLarge: TypeToken = TypeToken(36, FontWeight.Light, 1.2f)

    /** T02 `displayMedium` 32 / w300 / 1.2。 */
    public val displayMedium: TypeToken = TypeToken(32, FontWeight.Light, 1.2f)

    /** T03 `displaySmall`（欢迎语）28 / w300 / 1.25。 */
    public val displaySmall: TypeToken = TypeToken(28, FontWeight.Light, 1.25f)

    /** T04 `headlineLarge` 26 / w600 / 1.3。 */
    public val headlineLarge: TypeToken = TypeToken(26, FontWeight.SemiBold, 1.3f)

    /** T05 `headlineMedium` 22 / w600 / 1.3。 */
    public val headlineMedium: TypeToken = TypeToken(22, FontWeight.SemiBold, 1.3f)

    /** T06 `headlineSmall` 20 / w600 / 1.35。 */
    public val headlineSmall: TypeToken = TypeToken(20, FontWeight.SemiBold, 1.35f)

    /** T07 `titleLarge` 18 / w600 / 1.4。 */
    public val titleLarge: TypeToken = TypeToken(18, FontWeight.SemiBold, 1.4f)

    /** T08 `titleMedium` 16 / w600 / 1.45。 */
    public val titleMedium: TypeToken = TypeToken(16, FontWeight.SemiBold, 1.45f)

    /** T09 `titleSmall` 14 / w600 / 1.45。 */
    public val titleSmall: TypeToken = TypeToken(14, FontWeight.SemiBold, 1.45f)

    /** T10 `bodyLarge`（聊天正文）16 / w400 / 1.65。 */
    public val bodyLarge: TypeToken = TypeToken(16, FontWeight.Normal, 1.65f)

    /** T11 `bodyMedium` 14 / w400 / 1.55。 */
    public val bodyMedium: TypeToken = TypeToken(14, FontWeight.Normal, 1.55f)

    /** T12 `bodySmall` 12 / w400 / 1.5。 */
    public val bodySmall: TypeToken = TypeToken(12, FontWeight.Normal, 1.5f)

    /** T13 `labelLarge` 14 / w600 / 1.4，字距 0.1。 */
    public val labelLarge: TypeToken = TypeToken(14, FontWeight.SemiBold, 1.4f, 0.1f)

    /** T14 `labelMedium` 12 / w600 / 1.35，字距 0.2。 */
    public val labelMedium: TypeToken = TypeToken(12, FontWeight.SemiBold, 1.35f, 0.2f)

    /** T15 `labelSmall` 11 / w400 / 1.35，字距 0.2。 */
    public val labelSmall: TypeToken = TypeToken(11, FontWeight.Normal, 1.35f, 0.2f)

    /** Y54 顶栏会话名：`titleSmall` 覆盖为 15 / 1.3。 */
    public val headerTitle: TypeToken = TypeToken(15, FontWeight.SemiBold, 1.3f)

    /** Y55 顶栏状态文字：`labelSmall` 覆盖为 11 / 1.25。 */
    public val headerStatus: TypeToken = TypeToken(11, FontWeight.Normal, 1.25f)

    /** Y34 输入字号 / 行高 15 / 1.45。 */
    public val composerInput: TypeToken = TypeToken(15, FontWeight.Normal, 1.45f)

    /** Y61 模型胶囊文字：`labelMedium` 覆盖为 13。 */
    public val modelChip: TypeToken = TypeToken(13, FontWeight.SemiBold, 1.35f, 0.2f)

    /** ST50 过程行标签 `bodyMedium` 覆盖高度 1.3。 */
    public val streamRowLabel: TypeToken = TypeToken(14, FontWeight.Normal, 1.3f)

    /** ST50 行内细节 `bodySmall` 覆盖为 13 / 1.3。 */
    public val streamRowDetail: TypeToken = TypeToken(13, FontWeight.Normal, 1.3f)

    /** ST29 行式错误 `StreamRow.detail` 覆盖 height 1.45。 */
    public val noticeDetail: TypeToken = TypeToken(14, FontWeight.Normal, 1.45f)

    /** T16 `mono` 等宽正文 13 / 1.55（代码块、公式、HTML 字面）。 */
    public val mono: TypeToken = TypeToken(13, FontWeight.Normal, 1.55f)

    /** T17 `monoSmall` 等宽小号 12 / 1.5（代码井语言标识、脚注来源）。 */
    public val monoSmall: TypeToken = TypeToken(12, FontWeight.Normal, 1.5f)
}

/** §2 里没有归入间距阶梯、确实用到的尺寸与比例。 */
public object Metrics {
    /** Y11 用户气泡最大宽度 = 可用宽 × 0.82。 */
    public const val userBubbleWidthFraction: Float = 0.82f

    /** Y12 用户气泡内边距 16 / 8。 */
    public val userBubblePaddingH: Dp = 16.dp
    public val userBubblePaddingV: Dp = 8.dp

    /** Y24 / Y25 思考行 → 正文间距 8。 */
    public val thinkingGap: Dp = 8.dp

    /** Y26 中断标记：上距 8、图标 16、图标与字间距 4、行高 1.3。 */
    public val interruptedGapTop: Dp = 8.dp
    public val interruptedIcon: Dp = 16.dp
    public val interruptedIconGap: Dp = 4.dp
    public const val interruptedLineHeight: Float = 1.3f

    /** Y29 输入条圆角：关闭态 28、输入时 20。 */
    public val composerIdleRadius: Dp = Radii.xl
    public val composerExpandedRadius: Dp = Radii.lg

    /** Y30 键尺寸 36。 */
    public val composerKey: Dp = 36.dp

    /** Y31 键到胶囊边 6。 */
    public val composerKeyGap: Dp = 6.dp

    /** Y33 内边距：leading == null 时左 18，其余 6。 */
    public val composerPaddingStart: Dp = 18.dp
    public val composerPadding: Dp = 6.dp

    /** Y34/Y35 单行输入最少高度 36（= `_keySize`），文字垂直居中。 */
    public val composerFieldMinHeight: Dp = 36.dp

    /** Y36 最大可见行数 6。 */
    public const val composerMaxLines: Int = 6

    /** Y43/Y44 禁用键：`onSurface` 12% 底 / 38% 前景。 */
    public const val sendKeyDisabledFillAlpha: Float = 0.12f
    public const val sendKeyDisabledFgAlpha: Float = 0.38f

    /** Y45 图标尺寸 send 20 / stop 18。 */
    public val sendIcon: Dp = 20.dp
    public val stopIcon: Dp = 18.dp

    /** Y47 忙碌态圆形进度：内边距 11、strokeWidth 2。 */
    public val busyIndicatorPadding: Dp = 11.dp
    public val busyIndicatorStroke: Dp = 2.dp

    /** Y50 顶栏高度 56。 */
    public val headerBarHeight: Dp = 56.dp

    /** Y51 / Y52 标题胶囊高 44；块高 = 状态栏 inset + 栏高 + 8。 */
    public val headerPillHeight: Dp = 44.dp

    /** Y53 水平内边距 12。 */
    public val headerPaddingH: Dp = 12.dp

    /** Y56 状态圆点直径 6、与文字间距 5。 */
    public val statusDotHeader: Dp = 6.dp
    public val statusDotGap: Dp = 5.dp

    /** Y57 圆钮图标 22；容器取 44 触屏最小点按区（偏差已记）。 */
    public val headerIcon: Dp = 22.dp
    public val headerKey: Dp = 44.dp

    /** Y59 模型胶囊最小高 44。 */
    public val modelChipMinHeight: Dp = 44.dp

    /** Y60 模型胶囊内边距：左 12 / 右 8；chevron 16。 */
    public val modelChipPaddingStart: Dp = 12.dp
    public val modelChipPaddingEnd: Dp = 8.dp
    public val modelChipChevron: Dp = 16.dp

    /** Y61 名称与后缀间距 5。 */
    public val modelChipSuffixGap: Dp = 5.dp

    /** ST01 流式光标：宽 2.5 / 高 18 / 圆角 1.5，不透明度 1.0 ↔ 0.25。 */
    public val cursorWidth: Dp = 2.5.dp
    public val cursorHeight: Dp = 18.dp
    public val cursorRadius: Dp = 1.5.dp
    public const val cursorAlphaMax: Float = 1f
    public const val cursorAlphaMin: Float = 0.25f

    /** ST01 内联占位尺寸（`InlineTextContent` 只接受 sp）。 */
    public val cursorWidthSp: TextUnit = 2.5.sp
    public val cursorHeightSp: TextUnit = 18.sp

    /** ST04 / §4 等待指示：三个 6dp 圆点，间距 4，槽最小高 26。 */
    public const val typingDotCount: Int = 3
    public val typingDot: Dp = 6.dp
    public val typingDotGap: Dp = 4.dp
    public val typingDotMinHeight: Dp = 26.dp
    public const val typingDotAlphaMin: Float = 0.25f
    public const val typingDotAlphaMax: Float = 1f

    /** ST12 状态圆点呼吸不透明度 1 ↔ 0.35。 */
    public const val statusDotAlphaMin: Float = 0.35f
    public const val statusDotAlphaMax: Float = 1f

    /** ST27 错误横幅：图标 17、图标与文字 8、内边距 LTRB(12,4,4,4)、文字最多 3 行、底 α0.92。 */
    public val bannerIcon: Dp = 17.dp
    public val bannerIconGap: Dp = 8.dp
    public val bannerPaddingStart: Dp = 12.dp
    public val bannerPaddingTop: Dp = 4.dp
    public val bannerPaddingEnd: Dp = 4.dp
    public const val bannerMaxLines: Int = 3
    public const val bannerFillAlpha: Float = 0.92f

    /** B03 描边色 = accent α0.3。 */
    public const val bannerBorderAlpha: Float = 0.3f

    /** 原因行的弱化不透明度（ST27 只有一种文字色，弱化靠透明度）。 */
    public const val bannerCauseAlpha: Float = 0.78f

    /** 原因最多两行。 */
    public const val bannerCauseMaxLines: Int = 2

    /** ST27 操作按钮最小 48；关闭键 40 / 图标 18，关闭键底色 8%。 */
    public val bannerActionMinHeight: Dp = 48.dp
    public val bannerCloseKey: Dp = 40.dp
    public val bannerCloseIcon: Dp = 18.dp
    public const val bannerCloseWashAlpha: Float = 0.08f

    /** ST29 行式错误：图形块 14.4（= `lead × 0.6`）、缩进 32、底部 8。 */
    public val noticeLead: Dp = 14.4.dp
    public val noticeIndent: Dp = 32.dp
    public val noticeBottom: Dp = 8.dp

    /** ST45 过程行图形列宽 24。 */
    public val streamRowLead: Dp = 24.dp

    /** ST46 图形与文字间距 8。 */
    public val streamRowGap: Dp = 8.dp

    /** ST47 正文列起点 `indent` 32。 */
    public val streamRowIndent: Dp = 32.dp

    /** ST48 单行最小高 32。 */
    public val streamRowMinHeight: Dp = 32.dp

    /** ST09 思考图形：进行中 18、结束后 15，底透明度 10%。 */
    public val thinkingGlyphLive: Dp = 18.dp
    public val thinkingGlyphIdle: Dp = 15.dp
    public const val thinkingGlyphBgAlpha: Float = 0.1f

    /** 思考行右侧箭头；源码未给尺寸，取 16（偏差已记）。 */
    public val thinkingChevron: Dp = 16.dp

    /** ST16 空态左右边距 12 / 最大宽 696。 */
    public val emptyInset: Dp = 12.dp
    public val emptyMaxWidth: Dp = Space.floatingWidth

    /** ST18 副标题前 4（与 `bodyLarge` 三级色）。 */
    public val emptySubtitleGap: Dp = Space.xs

    /** ST19 副标题 → 胶囊 12。 */
    public val emptyPillGap: Dp = 12.dp

    /** ST21 胶囊 → 建议 28。 */
    public val emptySuggestionGap: Dp = 28.dp

    /** ST22 建议 → 提示行 6。 */
    public val emptyHintGap: Dp = 6.dp

    /** ST23 主胶囊：高 36、水平 14、垂直 6。 */
    public val emptyPrimaryHeight: Dp = 36.dp
    public val emptyPrimaryPaddingH: Dp = 14.dp
    public val emptyPrimaryPaddingV: Dp = 6.dp

    /** ST24 安静 chip 高 36；底 = `onSurface` 5%。 */
    public val emptyChipHeight: Dp = 36.dp
    public const val emptyChipFillAlpha: Float = 0.05f

    /** ST25 建议 chip 间距 8。 */
    public val suggestionGap: Dp = 8.dp

    /** ST26 建议 chip 半径 md=14、内边距 8。 */
    public val suggestionRadius: Dp = Radii.md
    public val suggestionPadding: Dp = 8.dp

    /** Y09 回到底部：底 `bottomInset + 12`、右 `max(0,(width−720)/2)+12`；按钮高 36、图标 16。 */
    public val scrollToBottomInset: Dp = 12.dp
    public val scrollToBottomHeight: Dp = 36.dp
    public val scrollToBottomIcon: Dp = 16.dp

    /** 对话框内边距（R05 圆角外的常规块间）。 */
    public val dialogPadding: Dp = Space.xl
}

/** 会话列表面板的量。DESIGN-SYSTEM 只覆盖聊天页（`§2`/`§3`），这里每一项都是「推断值」，无设计出处。 */
public object ListMetrics {
    /** 推断值：行最小高 56（Material 列表行惯例）。 */
    public val rowMinHeight: Dp = 56.dp

    /** 推断值：行内边距纵向 P02 `md`、横向 P04 `gutter`。 */
    public val rowPaddingV: Dp = Space.md
    public val rowPaddingH: Dp = Space.gutter

    /** 推断值：标题与时间间距取 P01 `xxs`。 */
    public val rowTitleGap: Dp = Space.xxs

    /** 推断值：整行圆角取 R03 `md`。 */
    public val rowRadius: Dp = Radii.md

    /** 推断值：删除键 44（触屏最小点按区，与顶栏圆钮同源）、图标 18。 */
    public val deleteKey: Dp = Metrics.headerKey
    public val deleteIcon: Dp = 18.dp

    /** 推断值：面板左右留白 P04 `gutter`、行间距 P01 `xs`。 */
    public val panelPaddingH: Dp = Space.gutter
    public val rowGap: Dp = Space.xs

    /** 推断值：空态图形 28、标题与提示间距 P02 `md`。 */
    public val emptyGlyph: Dp = 28.dp
    public val emptyGap: Dp = Space.md
}

/** §1.3 语义色 · 代码井（M13–M17）：等宽/代码/公式字面一律铺在 `codeWellBg` 上。 */
internal object LightCodeWell {
    /** M13 `codeWellBg` 代码井底。 */
    val bg: Color = Color(0xFFEDF0F6)

    /** M14 `codeWellHeader` 井头（语言标识条）。 */
    val header: Color = Color(0xFFE7EBF3)

    /** M15 `codeWellFg` 井内前景。 */
    val fg: Color = Color(0xFF1E232E)

    /** M16 `codeWellBorder` 井描边。 */
    val border: Color = Color(0xFFDBDEE5)

    /** M17 `lineNumberFg`（井头语言标识用同一灰阶）。 */
    val lineNumber: Color = Color(0xFF7D8088)
}

/** Markdown 正文的补充令牌：全部取自 DESIGN-SYSTEM 已有条目，未新增设计数值。 */
internal object RenderTokens {
    /** R01 `xs` 行内代码圆角 6〔`app/lib/ui/chat/render/markdown_body.dart`:177-178〕。 */
    val inlineCodeRadius: Dp = 6.dp

    /** R01 行内代码内边距 4 / 1〔同上〕。 */
    val inlineCodePaddingH: Dp = 4.dp
    val inlineCodePaddingV: Dp = 1.dp

    /** R02 `sm` 代码井圆角 10〔§7「代码块」条，`markdown_body.dart`:194-196〕。 */
    val codeWellRadius: Dp = 10.dp

    /** 代码井内边距 12 / 10 / 14 / 10〔`markdown_body.dart`:194-196〕。 */
    val codeWellPaddingStart: Dp = 12.dp
    val codeWellPaddingTop: Dp = 10.dp
    val codeWellPaddingEnd: Dp = 14.dp
    val codeWellPaddingBottom: Dp = 10.dp

    /** 表格 cell 内边距 12 / 8〔`markdown_body.dart`:227〕。 */
    val tableCellPaddingH: Dp = 12.dp
    val tableCellPaddingV: Dp = 8.dp

    /** `hr` 竖向内边距 12〔`markdown_body.dart`:165〕。 */
    val ruleMarginV: Dp = 12.dp

    /**
     * 块间距：§7 记「段落之间的间距由 Markdown 渲染库配置，未在源码找到」，
     * 取间距阶梯 P02 `md` = 12 作为既有条目。
     */
    val blockGap: Dp = Space.md

    /** 引用块左栏：宽取 B02 卡片描边 1，内容缩进取 P02 `md`。 */
    val quoteBarWidth: Dp = 1.dp
    val quoteIndent: Dp = Space.md

    /** 列表标记列最小宽：取 P02 `md`（有序号 3 位的对齐下限）。 */
    val listMarkerMinWidth: Dp = Space.md

    /** 嵌套一层列表/脚注的额外缩进：P05 `inTurn` = 8。 */
    val nestIndent: Dp = Space.sm

    /** §5 代码块可见行数上限（超出折叠）。 */
    const val codeVisibleLines: Int = 20_000
}

/** 代码井与公式字面用到的颜色角色（条目 ID 见 `LightCodeWell`）。 */
public data class NeoChatCodeColors(
    /** M13。 */
    public val wellBg: Color,
    /** M14。 */
    public val wellHeader: Color,
    /** M15。 */
    public val wellFg: Color,
    /** M16。 */
    public val wellBorder: Color,
    /** M17。 */
    public val lineNumber: Color,
)

/** 亮色的代码井角色。 */
public val NeoChatLightCodeColors: NeoChatCodeColors = NeoChatCodeColors(
    wellBg = LightCodeWell.bg,
    wellHeader = LightCodeWell.header,
    wellFg = LightCodeWell.fg,
    wellBorder = LightCodeWell.border,
    lineNumber = LightCodeWell.lineNumber,
)
