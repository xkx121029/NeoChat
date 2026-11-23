@file:Suppress("FunctionNaming")

package com.neochat.app.ui.chat

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import com.neochat.app.R
import com.neochat.app.data.ConversationSummary
import com.neochat.app.ui.theme.ListMetrics
import com.neochat.app.ui.theme.LocalNeoColors
import com.neochat.app.ui.theme.Metrics
import com.neochat.app.ui.theme.Space
import com.neochat.app.ui.theme.Stroke
import com.neochat.app.ui.theme.TypeScale
import com.neochat.app.ui.theme.neoStyle
import java.time.Instant
import java.time.ZoneId

/** 列表时间戳的四档显示（今天 / 昨天 / 今年 / 更早），与 Windows 端同一分档。 */
internal sealed interface ConversationTime {

    data class Today(val hour: Int, val minute: Int) : ConversationTime

    data object Yesterday : ConversationTime

    data class SameYear(val month: Int, val day: Int) : ConversationTime

    data class Older(val year: Int, val month: Int, val day: Int) : ConversationTime

    companion object {
        fun of(at: Long, now: Long, zone: ZoneId = ZoneId.systemDefault()): ConversationTime {
            val moment = Instant.ofEpochMilli(at).atZone(zone)
            val today = Instant.ofEpochMilli(now).atZone(zone).toLocalDate()
            return when {
                moment.toLocalDate() == today -> Today(moment.hour, moment.minute)
                moment.toLocalDate() == today.minusDays(1) -> Yesterday
                moment.year == today.year -> SameYear(moment.monthValue, moment.dayOfMonth)
                else -> Older(moment.year, moment.monthValue, moment.dayOfMonth)
            }
        }
    }
}

/**
 * 会话列表面板：系统返回键直接关闭（用全屏 Dialog 承载，不引入导航框架），
 * 顶栏左侧是返回键、右侧是新建键，中间是标题；行内右侧是删除键。
 */
@Composable
public fun ConversationPanel(
    conversations: List<ConversationSummary>,
    activeId: String?,
    now: Long,
    onSelect: (String) -> Unit,
    onNew: () -> Unit,
    onDelete: (String) -> Unit,
    onClose: () -> Unit,
) {
    Dialog(
        onDismissRequest = onClose,
        properties = DialogProperties(usePlatformDefaultWidth = false),
    ) {
        PanelBody(
            conversations = conversations,
            activeId = activeId,
            now = now,
            onSelect = onSelect,
            onNew = onNew,
            onDelete = onDelete,
            onClose = onClose,
        )
    }
}

@Composable
private fun PanelBody(
    conversations: List<ConversationSummary>,
    activeId: String?,
    now: Long,
    onSelect: (String) -> Unit,
    onNew: () -> Unit,
    onDelete: (String) -> Unit,
    onClose: () -> Unit,
) {
    val colors = LocalNeoColors.current
    var pendingDelete by remember { mutableStateOf<ConversationSummary?>(null) }
    Box(modifier = Modifier.fillMaxSize().background(colors.pageFallback)) {
        Column(modifier = Modifier.fillMaxSize()) {
            PanelHeader(onNew = onNew, onClose = onClose)
            HorizontalDivider(thickness = Stroke.hairline, color = colors.rule)
            if (conversations.isEmpty()) {
                PanelEmptyState()
            } else {
                LazyColumn(
                    modifier = Modifier.fillMaxSize(),
                    contentPadding = PaddingValues(
                        horizontal = ListMetrics.panelPaddingH,
                        vertical = Space.sm,
                    ),
                    verticalArrangement = Arrangement.spacedBy(ListMetrics.rowGap),
                ) {
                    items(items = conversations, key = { it.id }) { row ->
                        ConversationRow(
                            summary = row,
                            time = ConversationTime.of(row.lastActivityAt, now),
                            selected = row.id == activeId,
                            onClick = { onSelect(row.id) },
                            onDelete = { pendingDelete = row },
                        )
                    }
                }
            }
        }
    }
    pendingDelete?.let { row ->
        DeleteConfirm(
            onConfirm = {
                pendingDelete = null
                onDelete(row.id)
            },
            onCancel = { pendingDelete = null },
        )
    }
}

@Composable
private fun PanelHeader(onNew: () -> Unit, onClose: () -> Unit) {
    val colors = LocalNeoColors.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .height(Metrics.headerBarHeight)
            .padding(horizontal = Metrics.headerPaddingH),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(Space.sm),
    ) {
        PanelKey(icon = R.drawable.ic_close, description = stringResource(R.string.conversations_close), onClick = onClose)
        Text(
            text = stringResource(R.string.conversations_title),
            style = neoStyle(TypeScale.titleMedium).copy(color = colors.ink),
            modifier = Modifier.weight(1f),
        )
        PanelKey(
            icon = R.drawable.ic_new_chat,
            description = stringResource(R.string.chat_new),
            onClick = onNew,
        )
    }
}

@Composable
private fun PanelKey(icon: Int, description: String, onClick: () -> Unit) {
    val colors = LocalNeoColors.current
    Box(
        modifier = Modifier
            .size(Metrics.headerKey)
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

@Composable
private fun ConversationRow(
    summary: ConversationSummary,
    time: ConversationTime,
    selected: Boolean,
    onClick: () -> Unit,
    onDelete: () -> Unit,
) {
    val colors = LocalNeoColors.current
    val deleteLabel = stringResource(R.string.conversations_delete)
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .heightIn(min = ListMetrics.rowMinHeight)
            .clip(RoundedCornerShape(ListMetrics.rowRadius))
            .background(if (selected) colors.accentContainer else colors.pageFallback)
            .clickable(onClick = onClick)
            .padding(start = ListMetrics.rowPaddingH, top = ListMetrics.rowPaddingV)
            .padding(bottom = ListMetrics.rowPaddingV),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = summary.title.ifBlank { stringResource(R.string.conversations_untitled) },
                style = neoStyle(TypeScale.titleMedium).copy(
                    color = if (summary.title.isBlank()) colors.inkFaint else colors.ink,
                ),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Spacer(Modifier.height(ListMetrics.rowTitleGap))
            Text(
                text = timeLabel(time),
                style = neoStyle(TypeScale.bodySmall).copy(color = colors.inkFaint),
                maxLines = 1,
            )
        }
        Box(
            modifier = Modifier
                .size(ListMetrics.deleteKey)
                .semantics { contentDescription = deleteLabel }
                .clickable(onClick = onDelete),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                painter = painterResource(R.drawable.ic_trash),
                contentDescription = null,
                modifier = Modifier.size(ListMetrics.deleteIcon),
                tint = colors.inkFaint,
            )
        }
    }
}

@Composable
private fun timeLabel(time: ConversationTime): String = when (time) {
    is ConversationTime.Today -> stringResource(R.string.conversations_time_today, time.hour, time.minute)
    ConversationTime.Yesterday -> stringResource(R.string.conversations_time_yesterday)
    is ConversationTime.SameYear -> stringResource(R.string.conversations_time_date, time.month, time.day)
    is ConversationTime.Older -> stringResource(R.string.conversations_time_older, time.year, time.month, time.day)
}

@Composable
private fun PanelEmptyState() {
    val colors = LocalNeoColors.current
    Column(
        modifier = Modifier.fillMaxSize().padding(horizontal = ListMetrics.panelPaddingH),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
        Icon(
            painter = painterResource(R.drawable.ic_list),
            contentDescription = null,
            modifier = Modifier.size(ListMetrics.emptyGlyph),
            tint = colors.inkFaint,
        )
        Spacer(Modifier.height(ListMetrics.emptyGap))
        Text(
            text = stringResource(R.string.conversations_empty_title),
            style = neoStyle(TypeScale.titleMedium).copy(color = colors.inkMuted),
        )
        Spacer(Modifier.height(Space.xs))
        Text(
            text = stringResource(R.string.conversations_empty_hint),
            style = neoStyle(TypeScale.bodySmall).copy(color = colors.inkFaint),
        )
    }
}

@Composable
private fun DeleteConfirm(onConfirm: () -> Unit, onCancel: () -> Unit) {
    val colors = LocalNeoColors.current
    AlertDialog(
        onDismissRequest = onCancel,
        title = {
            Text(
                text = stringResource(R.string.conversations_delete),
                style = neoStyle(TypeScale.titleSmall).copy(color = colors.ink),
            )
        },
        text = {
            Text(
                text = stringResource(R.string.conversations_delete_confirm),
                style = neoStyle(TypeScale.bodyMedium).copy(color = colors.inkMuted),
            )
        },
        confirmButton = {
            TextButton(onClick = onConfirm) {
                Text(
                    text = stringResource(R.string.conversations_delete_action),
                    style = neoStyle(TypeScale.labelLarge).copy(color = colors.danger),
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onCancel) {
                Text(
                    text = stringResource(R.string.conversations_cancel),
                    style = neoStyle(TypeScale.labelLarge).copy(color = colors.inkMuted),
                )
            }
        },
        containerColor = colors.raisedSurface,
    )
}
