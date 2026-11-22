@file:Suppress("FunctionNaming")

package com.neochat.app.render

import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.InlineTextContent
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.Placeholder
import androidx.compose.ui.text.PlaceholderVerticalAlign
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.unit.Dp
import com.neochat.app.ui.theme.LocalNeoColors
import com.neochat.app.ui.theme.Metrics
import com.neochat.app.ui.theme.Motion
import com.neochat.app.ui.theme.RenderTokens
import com.neochat.app.ui.theme.Space
import com.neochat.app.ui.theme.TypeScale
import com.neochat.app.ui.theme.TypeToken
import com.neochat.app.ui.theme.neoStyle

/**
 * 助手正文的 Markdown 渲染入口（spec/render-model.md §4）：
 * 解析结果按块 id 复用视图（`key(block.id)`），流式时只在最后一个块末尾挂光标（ST01/ST02）。
 */
@Composable
public fun MarkdownBody(text: String, streaming: Boolean, modifier: Modifier = Modifier) {
    val cache = remember { StreamingMarkdownCache() }
    val model = remember(text, streaming) { cache.render(text, incomplete = streaming) }
    val lastIndex = model.blocks.lastIndex
    val truncatedColor = LocalNeoColors.current.inkFaint
    Column(modifier = modifier.fillMaxWidth()) {
        model.blocks.forEachIndexed { index, block ->
            key(block.id) {
                BlockSlot(
                    block = block,
                    topGap = index > 0,
                    cursor = streaming && index == lastIndex && wantsInlineCursor(block),
                )
            }
        }
        if (streaming && !wantsInlineCursor(model.blocks.lastOrNull())) {
            StreamCursorRow(Modifier.padding(top = RenderTokens.blockGap))
        }
        if (model.flags.truncated) {
            Text(
                text = RenderLabels.TRUNCATED,
                style = neoStyle(TypeScale.bodySmall).copy(color = truncatedColor),
                modifier = Modifier.padding(top = Space.sm),
            )
        }
    }
}

/** 光标能嵌在块内末尾的类型（ST02）；其余块用独立光标行。 */
private fun wantsInlineCursor(block: Block?): Boolean = when (block) {
    is Block.Paragraph, is Block.Heading, is Block.Code, is Block.Math, is Block.Html, is Block.Unknown -> true
    else -> false
}

/** 单块渲染；块间隔统一走 `RenderTokens.blockGap`。 */
@Composable
internal fun BlockSlot(block: Block, topGap: Boolean, cursor: Boolean) {
    val gap = if (topGap) RenderTokens.blockGap else Space.none
    when (block) {
        is Block.Paragraph -> InlineText(block.inlines, MaterialTheme.typography.bodyLarge, cursor, gap)
        is Block.Heading -> HeadingView(block, gap, cursor)
        is Block.Code -> CodeWellView(block.lang, block.text, gap, cursor)
        is Block.Math -> CodeWellView(RenderLabels.MATH, block.tex, gap, cursor)
        is Block.Mermaid -> CodeWellView(RenderLabels.MERMAID, block.src, gap, cursor)
        is Block.Html -> CodeWellView(RenderLabels.HTML, block.source, gap, cursor)
        is Block.Unknown -> CodeWellView("${RenderLabels.UNKNOWN} · ${block.kind}", block.raw, gap, cursor)
        else -> ContainerSlot(block, gap)
    }
}

/** 带容器的块（表格/列表/引用/分隔线/脚注/图片）。 */
@Composable
private fun ContainerSlot(block: Block, gap: Dp) {
    when (block) {
        is Block.Table -> TableView(block, gap)
        is Block.ListBlock -> ListView(block, gap)
        is Block.Quote -> QuoteView(block, gap)
        is Block.Rule -> RuleView(gap)
        is Block.FootnoteDef -> FootnoteView(block, gap)
        is Block.Image -> ImageView(block, gap)
        else -> Unit
    }
}

@Composable
private fun HeadingView(block: Block.Heading, gap: Dp, cursor: Boolean) {
    val colors = LocalNeoColors.current
    val color = if (block.level >= 6) colors.inkMuted else colors.ink
    InlineText(block.inlines, neoStyle(headingToken(block.level)), cursor, gap, color)
}

/** 标题层级映射：只用 §1.6 已有字阶，不新增字号（h6 用 `titleSmall` + 次级色）。 */
private fun headingToken(level: Int): TypeToken = when (level) {
    1 -> TypeScale.headlineLarge
    2 -> TypeScale.headlineSmall
    3 -> TypeScale.titleLarge
    4 -> TypeScale.titleMedium
    else -> TypeScale.titleSmall
}

/** 一段富文本；[cursor] 为真时在末尾内联一个流式光标（ST01）。 */
@Composable
internal fun InlineText(
    inlines: List<Inline>,
    style: TextStyle,
    cursor: Boolean,
    gap: Dp,
    color: Color = LocalNeoColors.current.ink,
) {
    val colors = LocalNeoColors.current
    val code = LocalNeoCodeColors.current
    val text = remember(inlines, cursor, colors, code) { buildInlineString(inlines, cursor, colors, code) }
    Text(
        text = text,
        style = style.copy(color = color),
        inlineContent = if (cursor) cursorContent() else emptyMap(),
        modifier = Modifier.fillMaxWidth().padding(top = gap),
    )
}

@Composable
private fun cursorContent(): Map<String, InlineTextContent> = mapOf(
    CURSOR_KEY to InlineTextContent(
        Placeholder(
            width = Metrics.cursorWidthSp,
            height = Metrics.cursorHeightSp,
            placeholderVerticalAlign = PlaceholderVerticalAlign.TextCenter,
        ),
    ) { StreamCursor() },
)

/** ST01 末尾光标，与 `ui/chat/StreamingParts.kt` 同值（宽 2.5 / 高 18 / 圆角 1.5）。 */
@Composable
internal fun StreamCursor() {
    val colors = LocalNeoColors.current
    val transition = rememberInfiniteTransition(label = "render-cursor")
    val alpha by transition.animateFloat(
        initialValue = Metrics.cursorAlphaMax,
        targetValue = Metrics.cursorAlphaMin,
        animationSpec = infiniteRepeatable(
            animation = tween(durationMillis = Motion.cursorBlinkMs, easing = Motion.std),
            repeatMode = RepeatMode.Reverse,
        ),
        label = "render-cursor-alpha",
    )
    Box(
        Modifier
            .size(Metrics.cursorWidth, Metrics.cursorHeight)
            .clip(RoundedCornerShape(Metrics.cursorRadius))
            .background(colors.streamingCursor.copy(alpha = alpha)),
    )
}

@Composable
private fun StreamCursorRow(modifier: Modifier = Modifier) {
    Row(modifier = modifier) { StreamCursor() }
}
