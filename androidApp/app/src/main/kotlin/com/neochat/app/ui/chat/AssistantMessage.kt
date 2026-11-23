@file:Suppress("FunctionNaming")

package com.neochat.app.ui.chat

import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.TextStyle
import com.neochat.app.R
import com.neochat.app.chat.ChatMessageUi
import com.neochat.app.render.MarkdownBody
import com.neochat.app.ui.theme.LocalNeoColors
import com.neochat.app.ui.theme.Metrics
import com.neochat.app.ui.theme.neoStyle
import com.neochat.app.ui.theme.TypeScale
import com.neochat.protocol.errors.ErrorAction
import com.neochat.protocol.errors.ErrorObject

/**
 * 助手消息（§2.1 Y23–Y28）：无框裸排、左对齐、占满正文列；思考行在上，正文在下，中断标记与错误跟着这一轮走。
 * 长按正文打开 §3.5 的消息菜单（ST34：长按气泡或正文）。
 */
@OptIn(ExperimentalFoundationApi::class)
@Composable
public fun AssistantMessage(
    message: ChatMessageUi,
    onAction: (ErrorObject, ErrorAction) -> Unit,
    onDismissError: (Long) -> Unit,
    onLongPress: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = LocalNeoColors.current
    Column(
        modifier = modifier
            .fillMaxWidth()
            .combinedClickable(onClick = {}, onLongClick = onLongPress),
    ) {
        if (message.reasoning.isNotBlank()) {
            ThinkingRow(
                messageId = message.id,
                reasoning = message.reasoning,
                streaming = message.streaming,
            )
        }
        when {
            message.text.isNotEmpty() -> MarkdownBody(
                text = message.text,
                streaming = message.streaming,
                modifier = Modifier.padding(top = Metrics.thinkingGap),
            )
            message.streaming -> TypingDots(modifier = Modifier.padding(top = Metrics.thinkingGap))
            else -> Unit
        }
        if (message.interrupted) {
            InterruptedMarker()
        }
        message.error?.let { error ->
            ErrorBanner(
                error = error,
                bannerVisible = message.errorBannerVisible,
                onAction = { action -> onAction(error, action) },
                onDismiss = { onDismissError(message.id) },
                modifier = Modifier.padding(top = Metrics.thinkingGap),
            )
        }
    }
}

/** Y26 中断标记：正文末尾，上距 8，图标 16 + 4 间距，bodySmall 1.3，danger 色。 */
@Composable
private fun InterruptedMarker() {
    val colors = LocalNeoColors.current
    Row(
        modifier = Modifier.padding(top = Metrics.interruptedGapTop),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Icon(
            painter = painterResource(R.drawable.ic_alert),
            contentDescription = null,
            modifier = Modifier.size(Metrics.interruptedIcon),
            tint = colors.danger,
        )
        Spacer(Modifier.width(Metrics.interruptedIconGap))
        Text(
            text = stringResource(R.string.message_interrupted),
            style = MaterialTheme.typography.bodySmall.copy(
                color = colors.danger,
                lineHeight = neoStyle(TypeScale.bodySmall).lineHeight * Metrics.interruptedLineHeight,
            ),
        )
    }
}
