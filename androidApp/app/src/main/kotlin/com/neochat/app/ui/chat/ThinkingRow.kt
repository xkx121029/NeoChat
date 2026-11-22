@file:Suppress("FunctionNaming")

package com.neochat.app.ui.chat

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.tween
import androidx.compose.animation.expandVertically
import androidx.compose.animation.shrinkVertically
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import com.neochat.app.R
import com.neochat.app.ui.theme.LocalNeoColors
import com.neochat.app.ui.theme.Metrics
import com.neochat.app.ui.theme.Motion
import com.neochat.app.ui.theme.Space
import com.neochat.app.ui.theme.neoStyle
import com.neochat.app.ui.theme.TypeScale

/**
 * 思考折叠行（§3.6 过程行几何 + ST09 图形）：
 * 24 图形列 / 8 间距 / 32 文字起点 / 最小高 32，点行展开全文，轮结束自动收回。
 */
@Composable
public fun ThinkingRow(
    messageId: Long,
    reasoning: String,
    streaming: Boolean,
    modifier: Modifier = Modifier,
) {
    val colors = LocalNeoColors.current
    var expanded by remember(messageId) { mutableStateOf(false) }
    LaunchedEffect(streaming) {
        if (!streaming) expanded = false
    }
    Column(modifier = modifier.fillMaxWidth()) {
        ThinkingHeader(streaming = streaming, expanded = expanded, onToggle = { expanded = !expanded })
        AnimatedVisibility(
            visible = expanded,
            enter = expandVertically(tween(durationMillis = Motion.collapseMs, easing = Motion.collapseCurve)),
            exit = shrinkVertically(tween(durationMillis = Motion.collapseMs, easing = Motion.collapseCurve)),
        ) {
            Text(
                text = reasoning,
                style = neoStyle(TypeScale.streamRowDetail).copy(color = colors.textTertiary),
                modifier = Modifier.padding(start = Metrics.streamRowIndent, top = Space.xs),
            )
        }
    }
}

/** ST45–ST50 的行头：图形 24 列、8 间距、标签 bodyMedium/次级色、右侧箭头。 */
@Composable
private fun ThinkingHeader(streaming: Boolean, expanded: Boolean, onToggle: () -> Unit) {
    val colors = LocalNeoColors.current
    val label = if (streaming) stringResource(R.string.thinking_live) else stringResource(R.string.thinking_done)
    val chevron = if (expanded) R.drawable.ic_chevron_down else R.drawable.ic_chevron_right
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .heightIn(min = Metrics.streamRowMinHeight)
            .clickable(onClick = onToggle)
            .padding(vertical = Space.xs),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(modifier = Modifier.width(Metrics.streamRowLead), contentAlignment = Alignment.Center) {
            Box(
                modifier = Modifier
                    .size(Metrics.streamRowLead)
                    .clip(CircleShape)
                    .background(colors.accent.copy(alpha = Metrics.thinkingGlyphBgAlpha)),
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    painter = painterResource(R.drawable.ic_spark),
                    contentDescription = null,
                    modifier = Modifier.size(
                        if (streaming) Metrics.thinkingGlyphLive else Metrics.thinkingGlyphIdle,
                    ),
                    tint = colors.accent,
                )
            }
        }
        Spacer(Modifier.width(Metrics.streamRowGap))
        Text(
            text = label,
            style = neoStyle(TypeScale.streamRowLabel).copy(color = colors.inkMuted),
            modifier = Modifier.weight(1f),
        )
        Icon(
            painter = painterResource(chevron),
            contentDescription = stringResource(
                if (expanded) R.string.thinking_collapse else R.string.thinking_expand,
            ),
            modifier = Modifier.size(Metrics.thinkingChevron),
            tint = colors.inkFaint,
        )
    }
}
