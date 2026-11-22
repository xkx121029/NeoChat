package com.neochat.app.render

import java.io.File
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/**
 * 验收用的"渲染清单"：把同一段消息解析出来，逐块输出类型 / id / 首行，
 * 落到 `androidApp/app/build/reports/render-manifest.txt`（同时打印到测试输出）。
 */
class RenderManifestTest {

    private val demo: String = listOf(
        "# 渲染样例",
        "",
        "这段包含**粗体**、*斜体*、~~删除线~~、`inlineCode` 与 [链接](https://example.com)。",
        "",
        "- 有序项一",
        "- 有序项二",
        "  - 嵌套项",
        "- [x] 已完成",
        "",
        "| 组件 | 状态 | 用例数 |",
        "| --- | :-: | ---: |",
        "| 解析器 | 完成 | 21 |",
        "| 渲染器 | 完成 | 3 |",
        "",
        "```kotlin",
        "val model = MarkdownParser().parse(text, incomplete = true)",
        "```",
        "",
        "\$\$E = mc^2\$\$",
        "",
        "> 引用里的 **重点**，以及行内公式 \$a^2+b^2\$。",
        "",
        "原始 HTML 行：<div onclick=\"alert(1)\">不执行</div>",
        "",
        "脚注引用[^1]，正文结束。",
        "",
        "[^1]: 这是脚注定义。",
    ).joinToString("\n")

    @Test
    fun `渲染清单：块类型序列、块 id 与每块内容`() {
        val model = MarkdownParser().parse(demo)
        val manifest = StringBuilder()
        manifest.append("blocks=").append(model.blocks.size)
            .append(" truncated=").append(model.flags.truncated)
            .append(" maxDepth=").append(model.flags.maxDepth).append('\n')
        append(manifest, model.blocks, 0)
        val file = File("build/reports/render-manifest.txt")
        file.parentFile?.mkdirs()
        file.writeText(manifest.toString())
        println(manifest.toString())

        assertTrue(model.flags.hasTable)
        assertTrue(model.flags.hasCode)
        assertTrue(model.flags.hasMath)
        assertTrue(model.blocks.first() is Block.Heading)
        assertEquals(model.blocks.size, model.blocks.map { it.id }.toSet().size)
        assertTrue(file.length() > 0)
    }

    private fun append(out: StringBuilder, blocks: List<Block>, depth: Int) {
        val pad = "  ".repeat(depth)
        for (block in blocks) {
            out.append(pad).append(block.id).append(' ').append(label(block)).append('\n')
            when (block) {
                is Block.Quote -> append(out, block.blocks, depth + 1)
                is Block.FootnoteDef -> append(out, block.blocks, depth + 1)
                is Block.ListBlock -> block.items.forEach { item ->
                    out.append(pad).append("  - item checked=").append(item.checked).append('\n')
                    append(out, item.blocks, depth + 2)
                }
                else -> Unit
            }
        }
    }

    private fun label(block: Block): String = when (block) {
        is Block.Paragraph -> "paragraph | ${inline(block.inlines)}"
        is Block.Heading -> "heading h${block.level} | ${inline(block.inlines)}"
        is Block.Code -> "code lang=${block.lang} closed=${block.closed} | ${block.text.lineSequence().first()}"
        is Block.Math -> "math closed=${block.closed} | ${block.tex}"
        is Block.Mermaid -> "mermaid kind=${block.kind} closed=${block.closed}"
        is Block.Table -> "table ${block.align.size}cols ${block.rows.size}rows | " +
            block.header.joinToString(" | ") { inline(it) }
        is Block.ListBlock -> "list ordered=${block.ordered} start=${block.start} tight=${block.tight} " +
            "items=${block.items.size}"
        is Block.Quote -> "quote blocks=${block.blocks.size}"
        is Block.Rule -> "hr"
        is Block.Html -> "html | ${block.source}"
        is Block.FootnoteDef -> "footnote_def label=${block.label}"
        is Block.Image -> "image src=${block.src} alt=${block.alt}"
        is Block.Unknown -> "unknown kind=${block.kind} | ${block.raw}"
    }

    private fun inline(inlines: List<Inline>): String = inlines.joinToString("") { item ->
        when (item) {
            is Inline.Text -> item.text
            is Inline.Emph -> "_${inline(item.inlines)}_"
            is Inline.Strong -> "**${inline(item.inlines)}**"
            is Inline.Strike -> "~~${inline(item.inlines)}~~"
            is Inline.Code -> "`${item.text}`"
            is Inline.Link -> "[${inline(item.inlines)}](${item.dest})"
            is Inline.Image -> "![${item.alt}](${item.src})"
            is Inline.Math -> "\$${item.tex}\$"
            is Inline.FootnoteRef -> "[^${item.label}]"
            Inline.Br -> "⏎"
        }
    }
}
