@file:Suppress("FunctionNaming")

package com.neochat.app.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.ProvidableCompositionLocal
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.style.LineHeightStyle
import com.neochat.app.render.LocalNeoCodeColors

/** 当前配色：亮色 `mist` 或暗色 `night`，由 [NeoChatTheme] 按系统深浅色注入。 */
public val LocalNeoColors: ProvidableCompositionLocal<NeoChatColors> = staticCompositionLocalOf {
    NeoChatLightColors
}

/** 行距分布 `even` 在 Compose 的近似（DESIGN-SYSTEM §5 差异 8）。 */
private val evenLeading: LineHeightStyle =
    LineHeightStyle(alignment = LineHeightStyle.Alignment.Center, trim = LineHeightStyle.Trim.None)

/** 令牌档位 → `TextStyle`（业务代码只有这一个构造入口）。 */
public fun neoStyle(token: TypeToken): TextStyle = TextStyle(
    fontSize = token.size,
    fontWeight = token.weight,
    lineHeight = token.lineHeight,
    letterSpacing = token.letterSpacing,
    lineHeightStyle = evenLeading,
)

/**
 * §1.1 / §1.3 / §1.4 手工构造 `ColorScheme`，不用 `ColorScheme.fromSeed`；
 * `surfaceTint` 显式透明，层次不靠给面染主色（§5 差异 10）。
 */
private fun neoLightColorScheme(c: NeoChatColors) = lightColorScheme(
    primary = c.accent,
    onPrimary = c.onAccent,
    primaryContainer = c.accentContainer,
    onPrimaryContainer = c.onAccentContainer,
    inversePrimary = c.accentPressed,
    secondary = c.slate,
    onSecondary = c.onAccent,
    secondaryContainer = c.slateContainer,
    onSecondaryContainer = c.onAccentContainer,
    tertiary = c.dusk,
    onTertiary = c.onAccent,
    tertiaryContainer = c.duskContainer,
    onTertiaryContainer = c.onDuskContainer,
    background = c.pageFallback,
    onBackground = c.ink,
    surface = c.pageFallback,
    onSurface = c.ink,
    surfaceVariant = c.raisedSurface,
    onSurfaceVariant = c.inkMuted,
    surfaceTint = Color.Transparent,
    inverseSurface = c.ink,
    inverseOnSurface = c.raisedSurface,
    error = c.danger,
    onError = c.onDanger,
    errorContainer = c.dangerContainer,
    onErrorContainer = c.onDangerContainer,
    outline = c.outline,
    outlineVariant = c.rule,
    scrim = c.scrim,
)

/** 暗色同一套角色位，取值换成 §1.2 / §1.3 / §1.4 的暗色列。 */
private fun neoDarkColorScheme(c: NeoChatColors) = darkColorScheme(
    primary = c.accent,
    onPrimary = c.onAccent,
    primaryContainer = c.accentContainer,
    onPrimaryContainer = c.onAccentContainer,
    inversePrimary = c.accentPressed,
    secondary = c.slate,
    onSecondary = c.onAccent,
    secondaryContainer = c.slateContainer,
    onSecondaryContainer = c.onAccentContainer,
    tertiary = c.dusk,
    onTertiary = c.onAccent,
    tertiaryContainer = c.duskContainer,
    onTertiaryContainer = c.onDuskContainer,
    background = c.pageFallback,
    onBackground = c.ink,
    surface = c.pageFallback,
    onSurface = c.ink,
    surfaceVariant = c.raisedSurface,
    onSurfaceVariant = c.inkMuted,
    surfaceTint = Color.Transparent,
    inverseSurface = c.ink,
    inverseOnSurface = c.raisedSurface,
    error = c.danger,
    onError = c.onDanger,
    errorContainer = c.dangerContainer,
    onErrorContainer = c.onDangerContainer,
    outline = c.outline,
    outlineVariant = c.rule,
    scrim = c.scrim,
)

/** §1.6 的 Material 字阶（业务代码只准用 `TypeScale` 或这里的档位）。 */
private val neoTypography: Typography = Typography(
    displayLarge = neoStyle(TypeScale.displayLarge),
    displayMedium = neoStyle(TypeScale.displayMedium),
    displaySmall = neoStyle(TypeScale.displaySmall),
    headlineLarge = neoStyle(TypeScale.headlineLarge),
    headlineMedium = neoStyle(TypeScale.headlineMedium),
    headlineSmall = neoStyle(TypeScale.headlineSmall),
    titleLarge = neoStyle(TypeScale.titleLarge),
    titleMedium = neoStyle(TypeScale.titleMedium),
    titleSmall = neoStyle(TypeScale.titleSmall),
    bodyLarge = neoStyle(TypeScale.bodyLarge),
    bodyMedium = neoStyle(TypeScale.bodyMedium),
    bodySmall = neoStyle(TypeScale.bodySmall),
    labelLarge = neoStyle(TypeScale.labelLarge),
    labelMedium = neoStyle(TypeScale.labelMedium),
    labelSmall = neoStyle(TypeScale.labelSmall),
)

/** §1.7 圆角（`RoundedCornerShape` 只能近似连续曲率，见 §5 差异 1）。 */
private val neoShapes: Shapes = Shapes(
    extraSmall = RoundedCornerShape(Radii.md),
    small = RoundedCornerShape(Radii.md),
    medium = RoundedCornerShape(Radii.lg),
    large = RoundedCornerShape(Radii.xl),
    extraLarge = RoundedCornerShape(Radii.xl),
)

/**
 * 应用主题：跟随系统深浅色（`isSystemInDarkTheme` 读 `LocalConfiguration`，
 * 运行中切换系统主题会触发重组，不需要重启 Activity），注入 Material 主题位、
 * `LocalNeoColors` 与渲染层的 `LocalNeoCodeColors`。
 */
@Composable
public fun NeoChatTheme(content: @Composable () -> Unit) {
    val dark = isSystemInDarkTheme()
    val colors = if (dark) NeoChatDarkColors else NeoChatLightColors
    val codeColors = if (dark) NeoChatDarkCodeColors else NeoChatLightCodeColors
    CompositionLocalProvider(
        LocalNeoColors provides colors,
        LocalNeoCodeColors provides codeColors,
    ) {
        MaterialTheme(
            colorScheme = if (dark) neoDarkColorScheme(colors) else neoLightColorScheme(colors),
            typography = neoTypography,
            shapes = neoShapes,
            content = content,
        )
    }
}
