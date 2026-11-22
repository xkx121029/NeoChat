@file:Suppress("FunctionNaming")

package com.neochat.app.ui.chat

import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.foundation.text.InlineTextContent
import androidx.compose.foundation.text.appendInlineContent
import androidx.compose.ui.text.Placeholder
import androidx.compose.ui.text.PlaceholderVerticalAlign
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.buildAnnotatedString
import com.neochat.app.ui.theme.LocalNeoColors
import com.neochat.app.ui.theme.Metrics
import com.neochat.app.ui.theme.Motion

/** ST01 末尾光标：呼吸竖线，嵌在正文末尾，跟随换行。 */
@Composable
private fun StreamingCursor() {
    val colors = LocalNeoColors.current
    val transition = rememberInfiniteTransition(label = "streaming-cursor")
    val alpha by transition.animateFloat(
        initialValue = Metrics.cursorAlphaMax,
        targetValue = Metrics.cursorAlphaMin,
        animationSpec = infiniteRepeatable(
            animation = tween(durationMillis = Motion.cursorBlinkMs, easing = Motion.std),
            repeatMode = RepeatMode.Reverse,
        ),
        label = "streaming-cursor-alpha",
    )
    Box(
        Modifier
            .size(Metrics.cursorWidth, Metrics.cursorHeight)
            .clip(RoundedCornerShape(Metrics.cursorRadius))
            .background(colors.streamingCursor.copy(alpha = alpha)),
    )
}

/**
 * 正文：流式时在末尾挂光标（ST01），结束后就是普通文本。
 * 光标用 `InlineTextContent` 占位，所以它会跟着自动换行。
 */
@Composable
public fun StreamingBody(text: String, streaming: Boolean, modifier: Modifier = Modifier) {
    val colors = LocalNeoColors.current
    val style: TextStyle = MaterialTheme.typography.bodyLarge.copy(color = colors.ink)
    if (!streaming) {
        Text(text = text, style = style, modifier = modifier)
        return
    }
    val annotated: AnnotatedString = buildAnnotatedString {
        append(text)
        appendInlineContent(CURSOR_KEY, CURSOR_PLACEHOLDER)
    }
    val inlineContent = mapOf(
        CURSOR_KEY to InlineTextContent(
            Placeholder(
                width = Metrics.cursorWidthSp,
                height = Metrics.cursorHeightSp,
                placeholderVerticalAlign = PlaceholderVerticalAlign.TextCenter,
            ),
        ) { StreamingCursor() },
    )
    Text(
        text = annotated,
        style = style,
        modifier = modifier,
        inlineContent = inlineContent,
    )
}

private const val CURSOR_KEY: String = "streaming-cursor"
private const val CURSOR_PLACEHOLDER: String = "\u200B"

/** ST04 降级实现：等待首个 token 的三点错峰（dsh-Go 的光球是自定义 shader，见 §5 差异 6）。 */
@Composable
public fun TypingDots(modifier: Modifier = Modifier) {
    val colors = LocalNeoColors.current
    val transition = rememberInfiniteTransition(label = "typing-dots")
    Row(
        modifier = modifier.height(Metrics.typingDotMinHeight),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(Metrics.typingDotGap),
    ) {
        repeat(Metrics.typingDotCount) { index ->
            val alpha by transition.animateFloat(
                initialValue = Metrics.typingDotAlphaMin,
                targetValue = Metrics.typingDotAlphaMax,
                animationSpec = infiniteRepeatable(
                    animation = tween(
                        durationMillis = Motion.statusDotBreathMs,
                        delayMillis = index * Motion.staggerMs,
                        easing = Motion.std,
                    ),
                    repeatMode = RepeatMode.Reverse,
                ),
                label = "typing-dot-$index",
            )
            Box(
                Modifier
                    .size(Metrics.typingDot)
                    .clip(CircleShape)
                    .background(colors.accent.copy(alpha = alpha)),
            )
        }
    }
}
