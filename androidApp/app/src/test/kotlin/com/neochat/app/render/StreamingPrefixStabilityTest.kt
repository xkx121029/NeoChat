package com.neochat.app.render

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/**
 * §4.5 前缀稳定性的可判定验证：把 200 字的混合 Markdown 逐字符追加，
 * 断言第 n 次追加后"除最后一块外"的块与最终模型逐字段相同；
 * 同时验证 §4.7 的增量缓存与全文解析完全等价。
 */
class StreamingPrefixStabilityTest {

    private val parser = MarkdownParser()

    private val document: String = listOf(
        "# 渲染样例",
        "段落含**粗体**、*斜体*、`码`、~~删~~与[链接](https://a.b)。",
        "- 甲",
        "- 乙",
        "> 引用",
        "| a | b |",
        "| :- | -: |",
        "| 11 | 2 |",
        "```js",
        "x(1)",
        "```",
        "\$\$x^2\$\$",
        "尾段 \$y\$ 与 ![图](/p.png)",
        "补充一句带 **加重** 与 `code` 的说明，正好凑满两百个字符，用于流式逐字追加的前缀稳定性测试，共六字符。",
    ).joinToString("\n")

    @Test
    fun `样本正好 200 字`() {
        assertEquals(200, document.length)
    }

    @Test
    fun `逐字符追加 200 次：前 n-1 块与最终模型逐字段相同`() {
        val final = parser.parse(document, incomplete = false)
        var previous = emptyList<Block>()
        for (end in 1..document.length) {
            val model = parser.parse(document.substring(0, end), incomplete = true)
            assertTrue(
                model.blocks.size <= final.blocks.size,
                "第 $end 个字符：块数 ${model.blocks.size} 超过最终 ${final.blocks.size}",
            )
            for (k in 0 until model.blocks.size - 1) {
                assertEquals(final.blocks[k], model.blocks[k], "第 $end 个字符：块 b$k 与最终模型不一致")
            }
            for (k in 0 until previous.size - 1) {
                assertEquals(previous[k], model.blocks[k], "第 $end 个字符：块 b$k 相对上一个前缀发生变化")
            }
            previous = model.blocks
        }
        assertEquals(final.blocks, parser.parse(document, incomplete = false).blocks)
    }

    @Test
    fun `增量缓存与全文解析逐字段相同`() {
        val cache = StreamingMarkdownCache()
        for (end in 1..document.length) {
            val prefix = document.substring(0, end)
            assertEquals(
                parser.parse(prefix, incomplete = true),
                cache.render(prefix, incomplete = true),
                "第 $end 个字符：增量结果与全量解析不一致",
            )
        }
        assertEquals(
            parser.parse(document, incomplete = false),
            cache.render(document, incomplete = false),
            "流结束时增量结果与全量解析不一致",
        )
    }

    @Test
    fun `未闭合围栏逐字追加不抛异常且始终是代码块`() {
        val fence = "```kotlin\nfun x() = 1"
        for (end in 1..fence.length) {
            assertTrue(parser.parse(fence.substring(0, end), incomplete = true).blocks.isNotEmpty())
        }
        val code = parser.parse(fence, incomplete = true).blocks.single() as Block.Code
        assertFalse(code.closed)
        assertEquals("kotlin", code.lang)
        assertEquals("fun x() = 1", code.text)
    }

    @Test
    fun `未闭合公式逐字追加不抛异常，闭合后 closed 翻转`() {
        val math = "\$\$\nE = mc^2"
        for (end in 1..math.length) {
            assertTrue(parser.parse(math.substring(0, end), incomplete = true).blocks.isNotEmpty())
        }
        assertFalse((parser.parse(math, incomplete = true).blocks.single() as Block.Math).closed)
        val closed = parser.parse("\$\$\nE = mc^2\n\$\$").blocks.single() as Block.Math
        assertTrue(closed.closed)
        assertEquals("E = mc^2", closed.tex)
    }
}
