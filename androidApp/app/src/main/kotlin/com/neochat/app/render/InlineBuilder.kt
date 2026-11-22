package com.neochat.app.render

import androidx.compose.foundation.text.appendInlineContent
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.LinkAnnotation
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.BaselineShift
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.withLink
import androidx.compose.ui.text.withStyle
import com.neochat.app.ui.theme.NeoChatCodeColors
import com.neochat.app.ui.theme.NeoChatColors
import com.neochat.app.ui.theme.TypeScale

/** 流式光标的内联占位键（每个 `Text` 自带一份 `inlineContent`，键可复用）。 */
internal const val CURSOR_KEY: String = "render-cursor"

private const val CURSOR_PLACEHOLDER: String = "\u200B"

/** §3 十种 Inline → `AnnotatedString`；[cursor] 为真时在末尾追加光标占位（ST01）。 */
internal fun buildInlineString(
    inlines: List<Inline>,
    cursor: Boolean,
    colors: NeoChatColors,
    code: NeoChatCodeColors,
): AnnotatedString = buildAnnotatedString {
    appendInlines(inlines, colors, code)
    if (cursor) appendInlineContent(CURSOR_KEY, CURSOR_PLACEHOLDER)
}

internal fun AnnotatedString.Builder.appendInlines(
    inlines: List<Inline>,
    colors: NeoChatColors,
    code: NeoChatCodeColors,
) {
    for (inline in inlines) {
        when (inline) {
            is Inline.Text -> append(inline.text)
            is Inline.Emph -> withStyle(SpanStyle(fontStyle = FontStyle.Italic)) {
                appendInlines(inline.inlines, colors, code)
            }
            is Inline.Strong -> withStyle(SpanStyle(fontWeight = FontWeight.SemiBold)) {
                appendInlines(inline.inlines, colors, code)
            }
            is Inline.Strike -> withStyle(SpanStyle(textDecoration = TextDecoration.LineThrough)) {
                appendInlines(inline.inlines, colors, code)
            }
            is Inline.Code -> withStyle(monoSpan(code)) { append(inline.text) }
            is Inline.Math -> withStyle(monoSpan(code)) { append(inline.tex) }
            is Inline.Link -> appendLink(inline, colors, code)
            is Inline.Image -> withStyle(SpanStyle(color = colors.inkMuted)) {
                append("${RenderLabels.IMAGE} ${inline.alt}".trim())
            }
            is Inline.FootnoteRef -> withStyle(
                SpanStyle(
                    fontSize = TypeScale.bodySmall.size,
                    baselineShift = BaselineShift.Superscript,
                    color = colors.inkFaint,
                ),
            ) { append("[${inline.label}]") }
            Inline.Br -> append('\n')
        }
    }
}

/** §7 链接白名单：名单外的只上色不挂链接，并在后面标注不可点击。 */
private fun AnnotatedString.Builder.appendLink(
    inline: Inline.Link,
    colors: NeoChatColors,
    code: NeoChatCodeColors,
) {
    if (RenderSafety.isAllowedLinkTarget(inline.dest)) {
        withLink(LinkAnnotation.Url(inline.dest)) {
            withStyle(SpanStyle(color = colors.accent, textDecoration = TextDecoration.Underline)) {
                appendInlines(inline.inlines, colors, code)
            }
        }
        return
    }
    withStyle(SpanStyle(color = colors.accent)) { appendInlines(inline.inlines, colors, code) }
    withStyle(SpanStyle(color = colors.inkFaint, fontSize = TypeScale.bodySmall.size)) {
        append(RenderLabels.LINK_BLOCKED)
    }
}

/** 等宽行内片段：DESIGN-SYSTEM §1.3 硬约束——等宽前景必须铺在 `codeWellBg` 上。 */
private fun monoSpan(code: NeoChatCodeColors): SpanStyle = SpanStyle(
    fontFamily = FontFamily.Monospace,
    fontSize = TypeScale.mono.size,
    background = code.wellBg,
    color = code.wellFg,
)
