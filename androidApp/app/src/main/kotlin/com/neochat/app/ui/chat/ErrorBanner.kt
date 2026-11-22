@file:Suppress("FunctionNaming")

package com.neochat.app.ui.chat

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextStyle
import com.neochat.app.R
import com.neochat.app.ui.theme.LocalNeoColors
import com.neochat.app.ui.theme.Metrics
import com.neochat.app.ui.theme.Radii
import com.neochat.app.ui.theme.Space
import com.neochat.app.ui.theme.Stroke
import com.neochat.app.ui.theme.neoStyle
import com.neochat.app.ui.theme.TypeScale
import com.neochat.protocol.errors.ErrorAction
import com.neochat.protocol.errors.ErrorObject

/**
 * 错误横幅（ST27）：标题 + 可能原因 + 每个 action 一个按钮 + 关闭。
 * 关掉后降级为行式错误（ST29），错误本身不消失。
 */
@Composable
public fun ErrorBanner(
    error: ErrorObject,
    bannerVisible: Boolean,
    onAction: (ErrorAction) -> Unit,
    onDismiss: () -> Unit,
    modifier: Modifier = Modifier,
) {
    if (!bannerVisible) {
        NoticeRow(error = error, modifier = modifier)
        return
    }
    val context = LocalContext.current
    val colors = LocalNeoColors.current
    val title = ErrorCopy.title(context, error)
    val causes = ErrorCopy.causes(context, error)
    val shape = RoundedCornerShape(Radii.md)
    Surface(
        modifier = modifier
            .fillMaxWidth()
            .semantics { liveRegion = LiveRegionMode.Assertive },
        shape = shape,
        color = colors.dangerContainer.copy(alpha = Metrics.bannerFillAlpha),
        border = BorderStroke(Stroke.errorBanner, colors.danger.copy(alpha = Metrics.bannerBorderAlpha)),
    ) {
        Row(
            modifier = Modifier.padding(
                start = Metrics.bannerPaddingStart,
                top = Metrics.bannerPaddingTop,
                end = Metrics.bannerPaddingEnd,
                bottom = Metrics.bannerPaddingTop,
            ),
            verticalAlignment = Alignment.Top,
        ) {
            Icon(
                painter = painterResource(R.drawable.ic_alert),
                contentDescription = null,
                modifier = Modifier
                    .padding(top = Space.xs)
                    .size(Metrics.bannerIcon),
                tint = colors.danger,
            )
            Spacer(Modifier.width(Metrics.bannerIconGap))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = title,
                    style = MaterialTheme.typography.bodySmall.copy(color = colors.onDangerContainer),
                    maxLines = Metrics.bannerMaxLines,
                )
                if (causes.isNotEmpty()) {
                    Text(
                        text = causes.joinToString(separator = " · "),
                        style = MaterialTheme.typography.bodySmall.copy(
                            color = colors.onDangerContainer.copy(alpha = Metrics.bannerCauseAlpha),
                        ),
                        maxLines = Metrics.bannerCauseMaxLines,
                    )
                }
                ErrorActions(error = error, onAction = onAction)
            }
            BannerClose(onDismiss = onDismiss)
        }
    }
}

/** 每个 action 都必须可点（spec/errors.md §2：`UI MUST 全部可点`）。 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun ErrorActions(error: ErrorObject, onAction: (ErrorAction) -> Unit) {
    val context = LocalContext.current
    val colors = LocalNeoColors.current
    if (error.actions.isEmpty()) return
    FlowRow(
        horizontalArrangement = Arrangement.spacedBy(Space.xs),
        verticalArrangement = Arrangement.spacedBy(Space.xxs),
    ) {
        error.actions.forEach { action ->
            val label = ErrorCopy.actionLabel(context, error, action)
            TextButton(
                onClick = { onAction(action) },
                modifier = Modifier.heightIn(min = Metrics.bannerActionMinHeight),
            ) {
                Text(
                    text = label,
                    style = neoStyle(TypeScale.labelSmall).copy(
                        color = colors.onDangerContainer,
                        fontWeight = TypeScale.labelSmall.weight,
                    ),
                )
            }
        }
    }
}

@Composable
private fun BannerClose(onDismiss: () -> Unit) {
    val colors = LocalNeoColors.current
    Box(
        modifier = Modifier
            .size(Metrics.bannerCloseKey)
            .clip(RoundedCornerShape(Radii.md))
            .background(colors.danger.copy(alpha = Metrics.bannerCloseWashAlpha))
            .clickableNoRipple(onClick = onDismiss),
        contentAlignment = Alignment.Center,
    ) {
        Icon(
            painter = painterResource(R.drawable.ic_close),
            contentDescription = stringResource(R.string.error_close),
            modifier = Modifier.size(Metrics.bannerCloseIcon),
            tint = colors.onDangerContainer,
        )
    }
}

/** ST29 行式错误：图形块 14.4 + 缩进 32 + 底部 8，文字 danger 色、行高 1.45。 */
@Composable
public fun NoticeRow(error: ErrorObject, modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val colors = LocalNeoColors.current
    val title = ErrorCopy.title(context, error)
    Row(
        modifier = modifier
            .fillMaxWidth()
            .padding(start = Metrics.noticeIndent, bottom = Metrics.noticeBottom),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(
            modifier = Modifier
                .size(Metrics.noticeLead)
                .clip(RoundedCornerShape(Radii.md))
                .background(colors.dangerContainer),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                painter = painterResource(R.drawable.ic_alert),
                contentDescription = null,
                modifier = Modifier.size(Metrics.noticeLead),
                tint = colors.danger,
            )
        }
        Spacer(Modifier.width(Metrics.bannerIconGap))
        Text(
            text = title,
            style = neoStyle(TypeScale.noticeDetail).copy(color = colors.danger),
            modifier = Modifier.weight(1f),
        )
    }
}

/** 无水波点按：横幅关闭键与胶囊用同一套按下缩放，不需要 ripple 的外溢。 */
internal fun Modifier.clickableNoRipple(onClick: () -> Unit): Modifier =
    this.clickable(interactionSource = null, indication = null, onClick = onClick)
