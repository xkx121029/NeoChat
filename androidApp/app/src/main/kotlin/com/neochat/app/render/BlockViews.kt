@file:Suppress("FunctionNaming")

package com.neochat.app.render

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import com.neochat.app.ui.theme.LocalNeoColors
import com.neochat.app.ui.theme.RenderTokens
import com.neochat.app.ui.theme.Space
import com.neochat.app.ui.theme.Stroke
import com.neochat.app.ui.theme.TypeScale
import com.neochat.app.ui.theme.neoStyle

/** 代码井（§2 `code` / `math` / `html` / `unknown`）：等宽 + 横向滚动，井头标注语言或降级说明。 */
@Composable
internal fun CodeWellView(lang: String?, body: String, gap: Dp, cursor: Boolean) {
    val code = LocalNeoCodeColors.current
    val visible = remember(body) { visibleCode(body) }
    Column(Modifier.fillMaxWidth().padding(top = gap)) {
        Surface(
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(RenderTokens.codeWellRadius),
            color = code.wellBg,
            border = BorderStroke(Stroke.hairline, code.wellBorder),
        ) {
            Column {
                if (!lang.isNullOrBlank()) {
                    Surface(color = code.wellHeader, modifier = Modifier.fillMaxWidth()) {
                        Text(
                            text = lang,
                            style = neoStyle(TypeScale.monoSmall).copy(color = code.lineNumber),
                            modifier = Modifier.padding(
                                start = RenderTokens.codeWellPaddingStart,
                                end = RenderTokens.codeWellPaddingEnd,
                                top = Space.xs,
                                bottom = Space.xs,
                            ),
                        )
                    }
                }
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .horizontalScroll(rememberScrollState())
                        .padding(
                            PaddingValues(
                                start = RenderTokens.codeWellPaddingStart,
                                top = RenderTokens.codeWellPaddingTop,
                                end = RenderTokens.codeWellPaddingEnd,
                                bottom = RenderTokens.codeWellPaddingBottom,
                            ),
                        ),
                ) {
                    Text(
                        text = visible,
                        style = neoStyle(TypeScale.mono).copy(
                            color = code.wellFg,
                            fontFamily = FontFamily.Monospace,
                        ),
                        softWrap = false,
                    )
                    if (cursor) {
                        Spacer(Modifier.width(Space.xs))
                        StreamCursor()
                    }
                }
            }
        }
    }
}

/** §5 代码块行数上限：超出部分不渲染（模型里的 `text` 仍保留全文）。 */
private fun visibleCode(body: String): String {
    if (body.count { it == '\n' } + 1 <= RenderTokens.codeVisibleLines) return body
    return body.lineSequence().take(RenderTokens.codeVisibleLines).joinToString("\n")
}

/** 真表格：等宽列、表头加粗、按 `align` 对齐、行间发丝线（§2 `table`）。 */
@Composable
internal fun TableView(block: Block.Table, gap: Dp) {
    val colors = LocalNeoColors.current
    Column(Modifier.fillMaxWidth().padding(top = gap)) {
        Row(Modifier.fillMaxWidth()) {
            block.header.forEachIndexed { index, cell ->
                TableCell(cell, neoStyle(TypeScale.titleSmall), block.align.getOrNull(index))
            }
        }
        HorizontalDivider(color = colors.rule, thickness = Stroke.hairline)
        block.rows.forEach { row ->
            Row(Modifier.fillMaxWidth()) {
                row.forEachIndexed { index, cell ->
                    TableCell(cell, neoStyle(TypeScale.bodyMedium), block.align.getOrNull(index))
                }
            }
            HorizontalDivider(color = colors.rule, thickness = Stroke.hairline)
        }
    }
}

@Composable
private fun RowScope.TableCell(inlines: List<Inline>, style: TextStyle, align: Align?) {
    val colors = LocalNeoColors.current
    val code = LocalNeoCodeColors.current
    val text = remember(inlines, colors, code) { buildInlineString(inlines, false, colors, code) }
    Text(
        text = text,
        style = style.copy(color = colors.ink, textAlign = textAlignOf(align)),
        modifier = Modifier
            .weight(1f)
            .padding(horizontal = RenderTokens.tableCellPaddingH, vertical = RenderTokens.tableCellPaddingV),
    )
}

private fun textAlignOf(align: Align?): TextAlign = when (align) {
    Align.CENTER -> TextAlign.Center
    Align.RIGHT -> TextAlign.End
    else -> TextAlign.Start
}

/** 列表（§2 `list`）：标记列固定最小宽，任务项用勾选字形。 */
@Composable
internal fun ListView(block: Block.ListBlock, gap: Dp) {
    val colors = LocalNeoColors.current
    val markerStyle = neoStyle(TypeScale.bodyLarge)
    Column(Modifier.fillMaxWidth().padding(top = gap)) {
        block.items.forEachIndexed { index, item ->
            Row(Modifier.fillMaxWidth()) {
                Text(
                    text = markerText(block, item, index),
                    style = markerStyle.copy(color = if (item.checked == true) colors.accent else colors.inkMuted),
                    modifier = Modifier
                        .widthIn(min = RenderTokens.listMarkerMinWidth)
                        .padding(end = Space.sm),
                )
                Column(Modifier.weight(1f)) {
                    item.blocks.forEachIndexed { childIndex, child ->
                        BlockSlot(child, topGap = childIndex > 0, cursor = false)
                    }
                }
            }
        }
    }
}

private fun markerText(block: Block.ListBlock, item: ListItem, index: Int): String = when {
    item.checked == true -> CHECKED
    item.checked == false -> UNCHECKED
    block.ordered -> "${block.start + index}."
    else -> BULLET_MARK
}

/** 引用块（§2 `quote`）：左侧竖线 + 内容缩进。 */
@Composable
internal fun QuoteView(block: Block.Quote, gap: Dp) {
    val colors = LocalNeoColors.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(top = gap)
            .height(IntrinsicSize.Min),
    ) {
        Box(
            Modifier
                .width(RenderTokens.quoteBarWidth)
                .fillMaxHeight()
                .background(colors.rule),
        )
        Column(Modifier.weight(1f).padding(start = RenderTokens.quoteIndent)) {
            block.blocks.forEachIndexed { index, child ->
                BlockSlot(child, topGap = index > 0, cursor = false)
            }
        }
    }
}

/** 分隔线（§2 `hr`）。 */
@Composable
internal fun RuleView(gap: Dp) {
    val colors = LocalNeoColors.current
    HorizontalDivider(
        color = colors.rule,
        thickness = Stroke.hairline,
        modifier = Modifier.padding(top = gap, bottom = RenderTokens.ruleMarginV),
    )
}

/** 脚注定义（§2 `footnote_def`）。 */
@Composable
internal fun FootnoteView(block: Block.FootnoteDef, gap: Dp) {
    val colors = LocalNeoColors.current
    Row(Modifier.fillMaxWidth().padding(top = gap)) {
        Text(
            text = "${RenderLabels.FOOTNOTE} ${block.label}",
            style = neoStyle(TypeScale.monoSmall).copy(color = colors.inkFaint),
            modifier = Modifier.padding(end = Space.sm),
        )
        Column(Modifier.weight(1f)) {
            block.blocks.forEachIndexed { index, child ->
                BlockSlot(child, topGap = index > 0, cursor = false)
            }
        }
    }
}

/** 块级图片（§2 `image`）：本期不加载位图，先给可读占位（远程图不自动加载，§7）。 */
@Composable
internal fun ImageView(block: Block.Image, gap: Dp) {
    val colors = LocalNeoColors.current
    val code = LocalNeoCodeColors.current
    Surface(
        modifier = Modifier.fillMaxWidth().padding(top = gap),
        shape = RoundedCornerShape(RenderTokens.codeWellRadius),
        color = code.wellBg,
        border = BorderStroke(Stroke.hairline, code.wellBorder),
    ) {
        Column(
            Modifier.padding(
                horizontal = RenderTokens.tableCellPaddingH,
                vertical = RenderTokens.tableCellPaddingV,
            ),
        ) {
            Text(
                text = block.alt.ifBlank { RenderLabels.IMAGE },
                style = neoStyle(TypeScale.bodyMedium).copy(color = colors.inkMuted),
            )
            Text(
                text = block.src,
                style = neoStyle(TypeScale.monoSmall).copy(color = colors.inkFaint),
            )
        }
    }
}

private const val CHECKED: String = "☑"
private const val UNCHECKED: String = "☐"
private const val BULLET_MARK: String = "•"
