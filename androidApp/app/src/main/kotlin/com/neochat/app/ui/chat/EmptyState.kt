@file:Suppress("FunctionNaming")

package com.neochat.app.ui.chat

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.style.TextAlign
import com.neochat.app.R
import com.neochat.app.ui.theme.LocalNeoColors
import com.neochat.app.ui.theme.Metrics
import com.neochat.app.ui.theme.Radii
import com.neochat.app.ui.theme.Space
import com.neochat.app.ui.theme.neoStyle
import com.neochat.app.ui.theme.TypeScale

/**
 * 空态（§3.3 ST16–ST26）：问候语 28/w300、副标题三级色、主胶囊 36 高、建议 chip 流式排布。
 * 这里没有"选项行"（模型/思考档位选择），M1 只保留主胶囊与建议。
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
public fun EmptyState(
    onStart: () -> Unit,
    onSuggestion: (String) -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = LocalNeoColors.current
    val suggestions = listOf(
        stringResource(R.string.empty_suggestion_summarize),
        stringResource(R.string.empty_suggestion_debug),
        stringResource(R.string.empty_suggestion_rewrite),
        stringResource(R.string.empty_suggestion_plan),
    )
    Column(
        modifier = modifier
            .fillMaxWidth()
            .padding(horizontal = Metrics.emptyInset)
            .widthIn(max = Metrics.emptyMaxWidth),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text(
            text = stringResource(R.string.empty_greeting),
            style = MaterialTheme.typography.displaySmall.copy(color = colors.ink),
            textAlign = TextAlign.Center,
        )
        Spacer(Modifier.height(Metrics.emptySubtitleGap))
        Text(
            text = stringResource(R.string.empty_subtitle),
            style = MaterialTheme.typography.bodyLarge.copy(color = colors.inkFaint),
            textAlign = TextAlign.Center,
        )
        Spacer(Modifier.height(Metrics.emptyPillGap))
        PrimaryPill(text = stringResource(R.string.empty_primary_action), onClick = onStart)
        Spacer(Modifier.height(Metrics.emptySuggestionGap))
        FlowRow(
            horizontalArrangement = Arrangement.spacedBy(
                Metrics.suggestionGap,
                Alignment.CenterHorizontally,
            ),
            verticalArrangement = Arrangement.spacedBy(Metrics.suggestionGap),
            modifier = Modifier.fillMaxWidth(),
        ) {
            suggestions.forEach { suggestion ->
                SuggestionChip(text = suggestion, onClick = { onSuggestion(suggestion) })
            }
        }
        Spacer(Modifier.height(Metrics.emptyHintGap))
        Text(
            text = stringResource(R.string.empty_hint),
            style = neoStyle(TypeScale.labelSmall).copy(color = colors.textTertiary),
            textAlign = TextAlign.Center,
        )
    }
}

/** ST23 主胶囊：36 高、水平 14 + 4、垂直 6、primary 底 + onPrimary 字。 */
@Composable
private fun PrimaryPill(text: String, onClick: () -> Unit) {
    val colors = LocalNeoColors.current
    Surface(
        modifier = Modifier
            .heightIn(min = Metrics.emptyPrimaryHeight)
            .clickableNoRipple(onClick = onClick),
        shape = RoundedCornerShape(Radii.xl),
        color = colors.accent,
    ) {
        Column(
            modifier = Modifier.padding(
                horizontal = Metrics.emptyPrimaryPaddingH,
                vertical = Metrics.emptyPrimaryPaddingV,
            ),
        ) {
            Text(
                text = text,
                style = MaterialTheme.typography.bodyMedium.copy(
                    color = colors.onAccent,
                    fontWeight = TypeScale.labelLarge.weight,
                ),
            )
        }
    }
}

/** ST26 建议 chip：`card` 半径 + 内边距 8；ST24 的安静底（`onSurface` 5%）。 */
@Composable
private fun SuggestionChip(text: String, onClick: () -> Unit) {
    val colors = LocalNeoColors.current
    Surface(
        modifier = Modifier
            .heightIn(min = Metrics.emptyChipHeight)
            .clickableNoRipple(onClick = onClick),
        shape = RoundedCornerShape(Metrics.suggestionRadius),
        color = colors.ink.copy(alpha = Metrics.emptyChipFillAlpha),
    ) {
        Column(modifier = Modifier.padding(all = Metrics.suggestionPadding)) {
            Text(
                text = text,
                style = MaterialTheme.typography.bodyMedium.copy(color = colors.inkMuted),
            )
        }
    }
}
