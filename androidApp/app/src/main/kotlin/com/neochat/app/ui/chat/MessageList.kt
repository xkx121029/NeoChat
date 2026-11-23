@file:Suppress("FunctionNaming")

package com.neochat.app.ui.chat

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.Dp
import com.neochat.app.R
import com.neochat.app.chat.ChatMessageUi
import com.neochat.app.chat.MessageRole
import com.neochat.app.chat.ScrollFollow
import com.neochat.app.ui.theme.LocalNeoColors
import com.neochat.app.ui.theme.Metrics
import com.neochat.app.ui.theme.Radii
import com.neochat.app.ui.theme.Space
import com.neochat.app.ui.theme.Stroke
import com.neochat.app.ui.theme.neoStyle
import com.neochat.app.ui.theme.TypeScale
import com.neochat.protocol.errors.ErrorAction
import com.neochat.protocol.errors.ErrorObject
import kotlinx.coroutines.launch

/**
 * 消息列表（§2.1）：阅读列 720 居中、轮内 8 / 轮间 24、顶部 8 / 底部 8。
 *
 * 贴底跟随交给 [ScrollFollow]：流式内容变高不算"用户上滚"，只有位置上移才解除跟随并
 * 露出 Y09 的"回到底部"。
 */
@Composable
public fun MessageList(
    messages: List<ChatMessageUi>,
    onAction: (ErrorObject, ErrorAction) -> Unit,
    onDismissError: (Long) -> Unit,
    onLongPressMessage: (ChatMessageUi) -> Unit,
    modifier: Modifier = Modifier,
) {
    val listState = rememberLazyListState()
    val scope = rememberCoroutineScope()
    BoxWithConstraints(modifier = modifier.fillMaxSize()) {
        val columnWidth = minOf(maxWidth - Space.gutter * 2, Space.readingWidth)
        val slackPx = with(LocalDensity.current) { Space.pinnedSlack.toPx() }
        val follow = remember(listState, slackPx) { ScrollFollow(slackPx) }
        val pinned = remember(follow) { mutableStateOf(follow.isFollowing) }
        val tailLength = messages.lastOrNull()?.let { it.text.length + it.reasoning.length } ?: 0

        LaunchedEffect(listState) {
            snapshotFlow {
                val info = listState.layoutInfo
                val last = info.visibleItemsInfo.lastOrNull()
                val position = listState.firstVisibleItemIndex.toLong() * ScrollFollow.POSITION_ITEM_SPAN +
                    listState.firstVisibleItemScrollOffset
                position.toFloat() to
                    if (last == null) 0f else (last.offset + last.size - info.viewportEndOffset).toFloat()
            }.collect { (position, distanceToBottom) ->
                follow.onScroll(position, distanceToBottom)
                pinned.value = follow.isFollowing
            }
        }

        LaunchedEffect(messages.size, tailLength) {
            if (follow.onContentChanged() && messages.isNotEmpty()) {
                listState.scrollToItem(messages.lastIndex, Int.MAX_VALUE)
            }
        }

        LazyColumn(
            state = listState,
            modifier = Modifier.fillMaxSize(),
            contentPadding = PaddingValues(top = Space.listTopInset, bottom = Space.listBottomInset),
        ) {
            itemsIndexed(messages, key = { _, message -> message.id }) { index, message ->
                Box(Modifier.fillMaxWidth().padding(top = gapBefore(messages, index))) {
                    Column(
                        modifier = Modifier
                            .align(Alignment.TopCenter)
                            .fillMaxWidth()
                            .padding(horizontal = Space.gutter)
                            .widthIn(max = Space.readingWidth),
                    ) {
                        if (message.role == MessageRole.USER) {
                            UserBubble(text = message.text, columnWidth = columnWidth)
                        } else {
                            AssistantMessage(
                                message = message,
                                onAction = onAction,
                                onDismissError = onDismissError,
                                onLongPress = { onLongPressMessage(message) },
                            )
                        }
                    }
                }
            }
        }
        if (!pinned.value && messages.isNotEmpty()) {
            ScrollToBottom(
                modifier = Modifier
                    .align(Alignment.BottomEnd)
                    .padding(
                        end = maxOf(Space.none, (maxWidth - Space.readingWidth) / 2) + Metrics.scrollToBottomInset,
                        bottom = Metrics.scrollToBottomInset,
                    ),
                onClick = {
                    follow.jumpToBottom()
                    pinned.value = true
                    scope.launch { listState.animateScrollToItem(messages.lastIndex, Int.MAX_VALUE) }
                },
            )
        }
    }
}

/** Y09 回到底部按钮：chrome 之外的轻件，用 pill 档（G13）而不是实色主按钮。 */
@Composable
private fun ScrollToBottom(modifier: Modifier = Modifier, onClick: () -> Unit) {
    val colors = LocalNeoColors.current
    Surface(
        modifier = modifier
            .heightIn(min = Metrics.scrollToBottomHeight)
            .clickableNoRipple(onClick = onClick),
        shape = RoundedCornerShape(Radii.xl),
        color = colors.pillFill,
        border = BorderStroke(Stroke.hairline, colors.pillHairline),
    ) {
        Row(
            modifier = Modifier.padding(horizontal = Space.md, vertical = Space.xs),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(Space.xs),
        ) {
            Icon(
                painter = painterResource(R.drawable.ic_arrow_down),
                contentDescription = null,
                modifier = Modifier.size(Metrics.scrollToBottomIcon),
                tint = colors.inkMuted,
            )
            Spacer(Modifier.width(Space.xxs))
            Text(
                text = stringResource(R.string.chat_scroll_to_bottom),
                style = neoStyle(TypeScale.labelSmall).copy(color = colors.inkMuted),
            )
        }
    }
}

/** Y05 / Y06：用户消息接用户消息 8，其余轮间 24。 */
private fun gapBefore(messages: List<ChatMessageUi>, index: Int): Dp {
    if (index == 0) return Space.none
    val previous = messages[index - 1]
    val current = messages[index]
    return if (previous.role == MessageRole.USER && current.role == MessageRole.USER) {
        Space.userToUser
    } else {
        Space.userToNonUser
    }
}
