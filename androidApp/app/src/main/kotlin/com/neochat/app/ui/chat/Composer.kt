@file:Suppress("FunctionNaming")

package com.neochat.app.ui.chat

import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.TextStyle
import com.neochat.app.R
import com.neochat.app.chat.ChatPhase
import com.neochat.app.chat.ChatUiState
import com.neochat.app.ui.theme.LocalNeoColors
import com.neochat.app.ui.theme.Metrics
import com.neochat.app.ui.theme.Motion
import com.neochat.app.ui.theme.Space
import com.neochat.app.ui.theme.Stroke
import com.neochat.app.ui.theme.neoStyle
import com.neochat.app.ui.theme.TypeScale

/**
 * 输入栏（§2.2）：chrome 玻璃 + 28 圆角（输入时展开为 20）、1–6 行、右下角 36 圆键。
 * 流式中圆键切到"停止"；连接中键内是圆形进度（ST11），但按下去仍然是取消（偏差见交付）。
 */
@Composable
public fun Composer(
    state: ChatUiState,
    onDraftChange: (String) -> Unit,
    onSend: () -> Unit,
    onStop: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = LocalNeoColors.current
    val radius by animateDpAsState(
        targetValue = if (state.draft.isNotEmpty()) Metrics.composerExpandedRadius else Metrics.composerIdleRadius,
        animationSpec = tween(durationMillis = Motion.standardMs, easing = Motion.std),
        label = "composer-radius",
    )
    val inputStyle: TextStyle = neoStyle(TypeScale.composerInput).copy(color = colors.ink)
    Surface(
        modifier = modifier.fillMaxWidth(),
        shape = RoundedCornerShape(radius),
        color = colors.chromeFill,
        border = BorderStroke(Stroke.hairline, colors.chromeHairline),
    ) {
        Row(
            modifier = Modifier.padding(
                start = Metrics.composerPaddingStart,
                top = Metrics.composerPadding,
                end = Metrics.composerPadding,
                bottom = Metrics.composerPadding,
            ),
            verticalAlignment = Alignment.Bottom,
            horizontalArrangement = Arrangement.spacedBy(Metrics.composerKeyGap),
        ) {
            DraftField(
                draft = state.draft,
                onDraftChange = onDraftChange,
                style = inputStyle,
                placeholder = stringResource(R.string.composer_placeholder),
            )
            if (state.draft.isNotEmpty()) {
                Text(
                    text = stringResource(R.string.composer_char_count, state.draft.length),
                    style = neoStyle(TypeScale.labelSmall).copy(color = colors.inkFaint),
                    modifier = Modifier.padding(bottom = Space.xs),
                )
            }
            SendKey(
                phase = state.phase,
                canSend = state.draft.isNotBlank(),
                onSend = onSend,
                onStop = onStop,
            )
        }
    }
}

/**
 * 输入框用字符串重载（新的 `BasicTextField(state: TextFieldState, …)` 之外的旧重载，已被标记废弃）。
 * 草稿的唯一真源在 ViewModel，换成 `TextFieldState` 要把光标位置也搬进状态层，不属于本里程碑；
 * 迁移与 `InputTransformation` 一起排进输入生态（见交付偏差）。
 */
@Suppress("DEPRECATION")
@Composable
private fun RowScope.DraftField(
    draft: String,
    onDraftChange: (String) -> Unit,
    style: TextStyle,
    placeholder: String,
) {
    val colors = LocalNeoColors.current
    Box(
        modifier = Modifier
            .weight(1f)
            .heightIn(min = Metrics.composerFieldMinHeight),
        contentAlignment = Alignment.CenterStart,
    ) {
        if (draft.isEmpty()) {
            Text(text = placeholder, style = style.copy(color = colors.inkFaint))
        }
        BasicTextField(
            value = draft,
            onValueChange = onDraftChange,
            modifier = Modifier.fillMaxWidth(),
            textStyle = style,
            cursorBrush = SolidColor(colors.accent),
            maxLines = Metrics.composerMaxLines,
        )
    }
}

@Composable
private fun SendKey(
    phase: ChatPhase,
    canSend: Boolean,
    onSend: () -> Unit,
    onStop: () -> Unit,
) {
    val colors = LocalNeoColors.current
    val interaction = remember { MutableInteractionSource() }
    val pressed by interaction.collectIsPressedAsState()
    val scale by animateFloatAsState(
        targetValue = if (pressed) Motion.sendKeyPressScale else 1f,
        animationSpec = tween(durationMillis = Motion.pressMs),
        label = "send-key-scale",
    )
    val active = phase == ChatPhase.CONNECTING || phase == ChatPhase.STREAMING
    val fill = when {
        active || canSend -> colors.accent
        else -> colors.ink.copy(alpha = Metrics.sendKeyDisabledFillAlpha)
    }
    val content = if (active || canSend) colors.onAccent else colors.ink.copy(alpha = Metrics.sendKeyDisabledFgAlpha)
    Box(
        modifier = Modifier
            .size(Metrics.composerKey)
            .graphicsLayer {
                scaleX = scale
                scaleY = scale
            }
            .clip(CircleShape)
            .background(fill)
            .clickable(
                interactionSource = interaction,
                indication = null,
                enabled = active || canSend,
                onClick = { if (active) onStop() else onSend() },
            ),
        contentAlignment = Alignment.Center,
    ) {
        SendKeyGlyph(phase = phase, tint = content)
    }
}

/** Y43–Y45：三种键面（进度 / 停止 / 发送），同一 36 圆键。 */
@Composable
private fun SendKeyGlyph(phase: ChatPhase, tint: androidx.compose.ui.graphics.Color) {
    when (phase) {
        ChatPhase.CONNECTING -> CircularProgressIndicator(
            modifier = Modifier
                .fillMaxSize()
                .padding(Metrics.busyIndicatorPadding),
            color = tint,
            strokeWidth = Metrics.busyIndicatorStroke,
        )
        ChatPhase.STREAMING -> Icon(
            painter = painterResource(R.drawable.ic_stop),
            contentDescription = stringResource(R.string.composer_stop),
            modifier = Modifier.size(Metrics.stopIcon),
            tint = tint,
        )
        else -> Icon(
            painter = painterResource(R.drawable.ic_send),
            contentDescription = stringResource(R.string.composer_send),
            modifier = Modifier.size(Metrics.sendIcon),
            tint = tint,
        )
    }
}
