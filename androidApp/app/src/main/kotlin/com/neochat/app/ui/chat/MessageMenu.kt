@file:Suppress("FunctionNaming")

package com.neochat.app.ui.chat

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextOverflow
import com.neochat.app.R
import com.neochat.app.chat.ChatMessageUi
import com.neochat.app.ui.theme.LocalNeoColors
import com.neochat.app.ui.theme.MenuMetrics
import com.neochat.app.ui.theme.Radii
import com.neochat.app.ui.theme.Space
import com.neochat.app.ui.theme.Stroke

/**
 * §3.5 长按菜单：顶部预览 → 组卡（本条消息 / 会重开这一轮）。
 *
 * 用 `ModalBottomSheet`（ST40 顶角 28、左右 gutter 20），把手按 ST41 画；工具条本身
 * 不做模糊（§1.5 只有 chrome / sheet 真做模糊，这里用不透明页面底兜底）。
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun MessageMenu(
    message: ChatMessageUi,
    codeBlocks: List<String>,
    onDismiss: () -> Unit,
    onCopyAll: () -> Unit,
    onCopyCode: (Int) -> Unit,
    onRegenerate: () -> Unit,
    onDelete: () -> Unit,
) {
    val colors = LocalNeoColors.current
    val actions = actionsFor(message, codeBlocks.size)
    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = rememberModalBottomSheetState(),
        containerColor = colors.pageFallback,
        contentColor = colors.ink,
        shape = RoundedCornerShape(topStart = Radii.xl, topEnd = Radii.xl),
        dragHandle = { MenuHandle() },
    ) {
        Column(modifier = Modifier.fillMaxWidth().padding(horizontal = MenuMetrics.sheetGutter)) {
            Text(
                text = message.text.ifBlank { stringResource(R.string.chat_menu_empty) },
                style = MaterialTheme.typography.bodyMedium.copy(color = colors.inkMuted),
                maxLines = MenuMetrics.previewMaxLines,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.padding(horizontal = MenuMetrics.previewExtra),
            )
            Spacer(Modifier.height(MenuMetrics.previewGap))
            Surface(
                color = colors.raisedSurface,
                shape = RoundedCornerShape(MenuMetrics.groupRadius),
                border = BorderStroke(Stroke.hairline, colors.rule),
            ) {
                Column {
                    actions.forEachIndexed { index, action ->
                        if (index > 0) {
                            HorizontalDivider(color = colors.rule, thickness = Stroke.hairline)
                        }
                        val danger = action.kind == MessageActionKind.DELETE
                        MenuRow(
                            icon = iconOf(action.kind),
                            label = labelOf(action, codeBlocks.size),
                            tint = if (danger) colors.danger else colors.ink,
                            onClick = {
                                when (action.kind) {
                                    MessageActionKind.COPY_ALL -> onCopyAll()
                                    MessageActionKind.COPY_CODE -> onCopyCode(action.codeIndex)
                                    MessageActionKind.REGENERATE -> onRegenerate()
                                    MessageActionKind.DELETE -> onDelete()
                                }
                            },
                        )
                    }
                }
            }
            Spacer(Modifier.height(MenuMetrics.sheetBottom))
        }
    }
}

/** ST41 把手：上 10、36×4 圆端、下 12，色 `onSurfaceVariant` α0.35。 */
@Composable
private fun MenuHandle() {
    val colors = LocalNeoColors.current
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .padding(top = MenuMetrics.handleTop, bottom = MenuMetrics.handleBottom),
        contentAlignment = Alignment.Center,
    ) {
        Box(
            Modifier
                .width(MenuMetrics.handleWidth)
                .height(MenuMetrics.handleHeight)
                .clip(RoundedCornerShape(percent = 50))
                .background(colors.inkMuted.copy(alpha = MenuMetrics.handleAlpha)),
        )
    }
}

/** 一行菜单项（§4「长按菜单」：行统一 48 高）。 */
@Composable
private fun MenuRow(icon: Int, label: String, tint: Color, onClick: () -> Unit) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .height(MenuMetrics.rowHeight)
            .clickable(onClick = onClick)
            .padding(horizontal = Space.lg),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Icon(
            painter = painterResource(icon),
            contentDescription = null,
            modifier = Modifier.size(MenuMetrics.rowIcon),
            tint = tint,
        )
        Spacer(Modifier.width(MenuMetrics.rowIconGap))
        Text(text = label, style = MaterialTheme.typography.bodyMedium.copy(color = tint))
    }
}

private fun iconOf(kind: MessageActionKind): Int = when (kind) {
    MessageActionKind.COPY_ALL, MessageActionKind.COPY_CODE -> R.drawable.ic_copy
    MessageActionKind.REGENERATE -> R.drawable.ic_regenerate
    MessageActionKind.DELETE -> R.drawable.ic_trash
}

@Composable
private fun labelOf(action: MessageAction, codeBlockCount: Int): String = when (action.kind) {
    MessageActionKind.COPY_ALL -> stringResource(R.string.chat_action_copy_all)
    MessageActionKind.COPY_CODE -> if (codeBlockCount > 1) {
        stringResource(R.string.chat_action_copy_code_index, action.codeIndex + 1)
    } else {
        stringResource(R.string.chat_action_copy_code)
    }
    MessageActionKind.REGENERATE -> stringResource(R.string.chat_action_regenerate)
    MessageActionKind.DELETE -> stringResource(R.string.chat_action_delete)
}
