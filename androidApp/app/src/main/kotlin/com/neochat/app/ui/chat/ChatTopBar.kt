@file:Suppress("FunctionNaming")

package com.neochat.app.ui.chat

import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.foundation.clickable
import com.neochat.app.R
import com.neochat.app.chat.ChatPhase
import com.neochat.app.ui.theme.LocalNeoColors
import com.neochat.app.ui.theme.Metrics
import com.neochat.app.ui.theme.Motion
import com.neochat.app.ui.theme.Radii
import com.neochat.app.ui.theme.Space
import com.neochat.app.ui.theme.Stroke
import com.neochat.app.ui.theme.neoStyle
import com.neochat.app.ui.theme.TypeScale

/**
 * 顶部栏（§2.3 Y50–Y58）：56 高、12 水平内边距、标题胶囊 44 高（会话名 15/1.3、状态 11/1.25）、
 * 状态点 6 + 5 间距，右侧是模型胶囊（Y59–Y62，点开即 Provider 配置）。
 */
@Composable
public fun ChatTopBar(
    phase: ChatPhase,
    modelName: String,
    onOpenList: () -> Unit,
    onOpenProvider: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = LocalNeoColors.current
    Row(
        modifier = modifier
            .fillMaxWidth()
            .height(Metrics.headerBarHeight)
            .padding(horizontal = Metrics.headerPaddingH),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(Space.sm),
    ) {
        HeaderKey(
            icon = R.drawable.ic_list,
            description = stringResource(R.string.conversations_open),
            onClick = onOpenList,
        )
        ChromePill(modifier = Modifier.weight(1f), shape = RoundedCornerShape(Radii.xl)) {
            Column(
                modifier = Modifier.padding(horizontal = Space.lg, vertical = Space.xs),
            ) {
                Text(
                    text = stringResource(R.string.chat_title),
                    style = neoStyle(TypeScale.headerTitle).copy(color = colors.ink),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                PhaseStatus(phase = phase)
            }
        }
        ChromePill(shape = RoundedCornerShape(Radii.xl)) {
            Row(
                modifier = Modifier
                    .heightIn(min = Metrics.modelChipMinHeight)
                    .clickable(onClick = onOpenProvider)
                    .padding(start = Metrics.modelChipPaddingStart, end = Metrics.modelChipPaddingEnd),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(Metrics.modelChipSuffixGap),
            ) {
                Text(
                    text = modelName,
                    style = neoStyle(TypeScale.modelChip).copy(color = colors.ink),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Icon(
                    painter = painterResource(R.drawable.ic_chevron_down),
                    contentDescription = stringResource(R.string.chat_model_settings),
                    modifier = Modifier.size(Metrics.modelChipChevron),
                    tint = colors.inkMuted,
                )
            }
        }
    }
}

/** ST13 状态语义：颜色不是唯一信号，旁边永远配文字。 */
@Composable
private fun PhaseStatus(phase: ChatPhase) {
    val colors = LocalNeoColors.current
    val label = stringResource(
        when (phase) {
            ChatPhase.IDLE -> R.string.phase_idle
            ChatPhase.CONNECTING -> R.string.phase_connecting
            ChatPhase.STREAMING -> R.string.phase_streaming
            ChatPhase.STOPPED -> R.string.phase_stopped
            ChatPhase.FAILED -> R.string.phase_failed
        },
    )
    val tone = when (phase) {
        ChatPhase.IDLE -> colors.outline
        ChatPhase.CONNECTING, ChatPhase.STREAMING -> colors.accent
        ChatPhase.STOPPED -> colors.warning
        ChatPhase.FAILED -> colors.danger
    }
    Row(verticalAlignment = Alignment.CenterVertically) {
        StatusDot(color = tone, breathing = phase == ChatPhase.CONNECTING || phase == ChatPhase.STREAMING)
        Spacer(Modifier.width(Metrics.statusDotGap))
        Text(
            text = label,
            style = neoStyle(TypeScale.headerStatus).copy(color = colors.inkFaint),
            maxLines = 1,
        )
    }
}

/** ST12 状态圆点：busy 时透明度 1 ↔ 0.35，周期 1200ms。 */
@Composable
private fun StatusDot(color: androidx.compose.ui.graphics.Color, breathing: Boolean) {
    val alpha = if (breathing) {
        val transition = rememberInfiniteTransition(label = "status-dot")
        val animated by transition.animateFloat(
            initialValue = Metrics.statusDotAlphaMax,
            targetValue = Metrics.statusDotAlphaMin,
            animationSpec = infiniteRepeatable(
                animation = tween(durationMillis = Motion.statusDotBreathMs, easing = Motion.std),
                repeatMode = RepeatMode.Reverse,
            ),
            label = "status-dot-alpha",
        )
        animated
    } else {
        Metrics.statusDotAlphaMax
    }
    Box(
        modifier = Modifier
            .size(Metrics.statusDotHeader)
            .clip(CircleShape)
            .background(color.copy(alpha = alpha)),
    )
}

@Composable
private fun HeaderKey(icon: Int, description: String, onClick: () -> Unit) {
    val colors = LocalNeoColors.current
    Box(
        modifier = Modifier
            .size(Metrics.headerKey)
            .clip(CircleShape)
            .semantics { contentDescription = description }
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Icon(
            painter = painterResource(icon),
            contentDescription = null,
            modifier = Modifier.size(Metrics.headerIcon),
            tint = colors.inkMuted,
        )
    }
}

/** chrome 玻璃胶囊：`G01` fill + `G03` 发丝边（不做模糊，见 §6 M1 之后第 3 条）。 */
@Composable
private fun ChromePill(
    modifier: Modifier = Modifier,
    shape: RoundedCornerShape,
    content: @Composable () -> Unit,
) {
    val colors = LocalNeoColors.current
    Box(
        modifier = modifier
            .heightIn(min = Metrics.headerPillHeight)
            .clip(shape)
            .background(colors.chromeFill)
            .border(BorderStroke(Stroke.hairline, colors.chromeHairline), shape),
        contentAlignment = Alignment.CenterStart,
    ) {
        content()
    }
}
