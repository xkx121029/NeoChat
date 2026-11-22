package com.neochat.app.render

import androidx.compose.runtime.ProvidableCompositionLocal
import androidx.compose.runtime.staticCompositionLocalOf
import com.neochat.app.ui.theme.NeoChatCodeColors
import com.neochat.app.ui.theme.NeoChatLightCodeColors

/**
 * 代码井/等宽字面用的颜色角色（DESIGN-SYSTEM §1.3 M13–M17）。
 *
 * 默认值即亮色 `mist` 的实例；暗色接入时由主题层 provide（`ui/theme/NeoChatTheme.kt`
 * 本期不属于本任务的改动范围，故 locale 定义在渲染层）。
 */
public val LocalNeoCodeColors: ProvidableCompositionLocal<NeoChatCodeColors> = staticCompositionLocalOf {
    NeoChatLightCodeColors
}
