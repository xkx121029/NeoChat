package com.neochat.app.render

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/** spec/render-model.md §2 / §3 逐类型的解析用例，外加 §4 未闭合与 §7 安全规则的用例。 */
class MarkdownParserTest {

    private val parser = MarkdownParser()

    @Test
    fun `paragraph 段落：软换行归一为空格`() {
        val block = parser.parse("第一行\n第二行").blocks.single() as Block.Paragraph
        assertEquals("b0", block.id)
        assertEquals(listOf(Inline.Text("第一行 第二行")), block.inlines)
    }

    @Test
    fun `heading 标题：ATX 六级与 Setext 同级类型`() {
        val atx = parser.parse("# 一\n\n### 三\n\n###### 六").blocks
        assertEquals(listOf(1, 3, 6), atx.map { (it as Block.Heading).level })
        val setext = parser.parse("标题\n===\n\n小标题\n---").blocks
        assertEquals(listOf(1, 2), setext.map { (it as Block.Heading).level })
        assertEquals(listOf(Inline.Text("标题")), (setext[0] as Block.Heading).inlines)
    }

    @Test
    fun `code 代码围栏：lang 与 info string 的 meta`() {
        val block = parser.parse("```kotlin filename=Main.kt ln\nval x = 1\n```").blocks.single() as Block.Code
        assertEquals("kotlin", block.lang)
        assertEquals("Main.kt", block.meta.filename)
        assertTrue(block.meta.lineNumbers)
        assertTrue(block.closed)
        assertEquals("val x = 1", block.text)
    }

    @Test
    fun `code 未闭合围栏：closed=false，内容原样保留`() {
        val block = parser.parse("```python\nprint(1)").blocks.single() as Block.Code
        assertFalse(block.closed)
        assertEquals("python", block.lang)
        assertEquals("print(1)", block.text)
    }

    @Test
    fun `code info string 无法识别的属性被忽略`() {
        val block = parser.parse("```\nnolanguage\n```").blocks.single() as Block.Code
        assertNull(block.lang)
        assertNull(block.meta.filename)
        assertFalse(block.meta.lineNumbers)
    }

    @Test
    fun `math 块级公式：闭合与非闭合`() {
        val closed = parser.parse("$$\nE = mc^2\n$$").blocks.single() as Block.Math
        assertTrue(closed.closed)
        assertEquals("E = mc^2", closed.tex)
        val open = parser.parse("\\[\na^2 + b^2\n").blocks.single() as Block.Math
        assertFalse(open.closed)
        assertEquals("a^2 + b^2", open.tex)
    }

    @Test
    fun `mermaid 围栏：flowchart 与 sequence 保留类型，other 降级为 code`() {
        val flow = parser.parse("```mermaid\ngraph TD\nA --> B\n```").blocks.single() as Block.Mermaid
        assertEquals(MermaidKind.FLOWCHART, flow.kind)
        assertTrue(flow.closed)
        val seq = parser.parse("```mermaid\nsequenceDiagram\nA->>B: 你好\n```").blocks.single() as Block.Mermaid
        assertEquals(MermaidKind.SEQUENCE, seq.kind)
        val other = parser.parse("```mermaid\npie\n```").blocks.single() as Block.Code
        assertEquals("mermaid", other.lang)
    }

    @Test
    fun `quote 引用块：可嵌套`() {
        val outer = parser.parse("> 引用第一行\n> 第二行").blocks.single() as Block.Quote
        assertEquals(1, outer.blocks.size)
        assertEquals("引用第一行 第二行", ((outer.blocks[0] as Block.Paragraph).inlines.single() as Inline.Text).text)
        val nested = parser.parse("> 外层\n> > 内层").blocks.single() as Block.Quote
        assertEquals(2, nested.blocks.size)
        val inner = nested.blocks[1] as Block.Quote
        assertEquals(listOf(Inline.Text("内层")), (inner.blocks.single() as Block.Paragraph).inlines)
    }

    @Test
    fun `hr 分隔线与其他块独立`() {
        val blocks = parser.parse("上\n\n---\n\n下").blocks
        assertEquals(3, blocks.size)
        assertTrue(blocks[1] is Block.Rule)
    }

    @Test
    fun `list 无序列表：tight 与嵌套`() {
        val list = parser.parse("- 一\n- 二\n  - 二点一").blocks.single() as Block.ListBlock
        assertFalse(list.ordered)
        assertTrue(list.tight)
        assertEquals(2, list.items.size)
        assertEquals(2, list.items[1].blocks.size)
        assertTrue(list.items[1].blocks[1] is Block.ListBlock)
    }

    @Test
    fun `list 有序列表：start 取自首个序号`() {
        val list = parser.parse("3. 三\n4. 四").blocks.single() as Block.ListBlock
        assertTrue(list.ordered)
        assertEquals(3, list.start)
        assertEquals(2, list.items.size)
    }

    @Test
    fun `list 任务列表：checked 非 null`() {
        val list = parser.parse("- [x] 完成\n- [ ] 待办").blocks.single() as Block.ListBlock
        assertEquals(true, list.items[0].checked)
        assertEquals(false, list.items[1].checked)
    }

    @Test
    fun `list 空行把列表变松散`() {
        val list = parser.parse("- 一\n\n- 二").blocks.single() as Block.ListBlock
        assertFalse(list.tight)
        assertEquals(2, list.items.size)
    }

    @Test
    fun `table 表格：对齐、表头与转义竖线`() {
        val block = parser.parse("| 名 | 量 | 价 |\n| --- | :-: | ---: |\n| a\\|b | 2 | 3 |").blocks
            .single() as Block.Table
        assertEquals(listOf(null, Align.CENTER, Align.RIGHT), block.align)
        assertEquals(3, block.header.size)
        assertEquals(listOf(Inline.Text("a|b")), block.rows.single()[0])
        assertEquals(1, block.rows.size)
    }

    @Test
    fun `table 列数不一致时不当表格`() {
        val blocks = parser.parse("| a | b |\n| --- |\n| 1 | 2 |").blocks
        assertFalse(blocks.single() is Block.Table)
    }

    @Test
    fun `inline code 与强调三兄弟`() {
        val block = parser.parse("**粗** *斜* ~~删~~ `码`").blocks.single() as Block.Paragraph
        assertEquals(7, block.inlines.size)
        assertEquals(Inline.Strong(listOf(Inline.Text("粗"))), block.inlines[0])
        assertEquals(Inline.Emph(listOf(Inline.Text("斜"))), block.inlines[2])
        assertEquals(Inline.Strike(listOf(Inline.Text("删"))), block.inlines[4])
        assertEquals(Inline.Code("码"), block.inlines[6])
    }

    @Test
    fun `link 行内链接：dest 与 title`() {
        val block = parser.parse("看 [例子](https://example.com \"标题\") 吧").blocks.single() as Block.Paragraph
        val link = block.inlines.filterIsInstance<Inline.Link>().single()
        assertEquals("https://example.com", link.dest)
        assertEquals("标题", link.title)
        assertEquals(listOf(Inline.Text("例子")), link.inlines)
    }

    @Test
    fun `link 非白名单协议被安全规则拒绝`() {
        val block = parser.parse("[点我](javascript:alert(1))").blocks.single() as Block.Paragraph
        val link = block.inlines.filterIsInstance<Inline.Link>().single()
        assertEquals("javascript:alert(1)", link.dest)
        assertFalse(RenderSafety.isAllowedLinkTarget(link.dest))
        assertTrue(RenderSafety.isAllowedLinkTarget("mailto:a@b.c"))
        assertFalse(RenderSafety.isAllowedLinkTarget("/relative/path"))
    }

    @Test
    fun `image 单图独占一段时成块级图片`() {
        val block = parser.parse("![示意图](/img/a.png \"图注\")").blocks.single() as Block.Image
        assertEquals("/img/a.png", block.src)
        assertEquals("示意图", block.alt)
        assertEquals("图注", block.title)
    }

    @Test
    fun `image 与其他文字同段时是行内图片`() {
        val block = parser.parse("看图 ![图](/a.png) 结束").blocks.single() as Block.Paragraph
        assertTrue(block.inlines.any { it is Inline.Image })
        assertEquals(3, block.inlines.size)
    }

    @Test
    fun `math 行内公式：美元符与反斜杠括号`() {
        val block = parser.parse("质能 \$E=mc^2\$ 与 \\(a+b\\)").blocks.single() as Block.Paragraph
        val maths = block.inlines.filterIsInstance<Inline.Math>()
        assertEquals(listOf("E=mc^2", "a+b"), maths.map { it.tex })
    }

    @Test
    fun `footnote 定义与引用`() {
        val blocks = parser.parse("正文[^1]\n\n[^1]: 注解文字").blocks
        val paragraph = blocks[0] as Block.Paragraph
        assertEquals(Inline.FootnoteRef("1"), paragraph.inlines.last())
        val def = blocks[1] as Block.FootnoteDef
        assertEquals("1", def.label)
        assertEquals("注解文字", ((def.blocks.single() as Block.Paragraph).inlines.single() as Inline.Text).text)
        assertTrue(parser.parse("正文[^1]\n\n[^1]: 注解").flags.hasFootnote)
    }

    @Test
    fun `html 行首原始 HTML 成块且保持字面`() {
        val blocks = parser.parse("<div onclick=\"alert(1)\">hi</div>\n\n后文").blocks
        val html = blocks[0] as Block.Html
        assertEquals("<div onclick=\"alert(1)\">hi</div>", html.source)
        assertTrue(blocks[1] is Block.Paragraph)
    }

    @Test
    fun `html 行内原始 HTML 保留为字面文本`() {
        val block = parser.parse("a <b>粗</b> c").blocks.single() as Block.Paragraph
        assertEquals(listOf(Inline.Text("a <b>粗</b> c")), block.inlines)
    }

    @Test
    fun `unknown 引用式链接不解析：方括号原样保留，定义归入 unknown`() {
        val blocks = parser.parse("[文字][label]\n\n[label]: https://example.com").blocks
        assertEquals(listOf(Inline.Text("[文字][label]")), (blocks[0] as Block.Paragraph).inlines)
        val unknown = blocks[1] as Block.Unknown
        assertEquals("link_definition", unknown.kind)
    }

    @Test
    fun `br 行尾两空格成硬换行`() {
        val block = parser.parse("上  \n下").blocks.single() as Block.Paragraph
        assertEquals(listOf(Inline.Text("上"), Inline.Br, Inline.Text("下")), block.inlines)
    }

    @Test
    fun `block id 按块序号稳定分配`() {
        val ids = parser.parse("# 一\n\n正文\n\n- 项").blocks.map { it.id }
        assertEquals(listOf("b0", "b1", "b2"), ids)
        val quote = parser.parse("> 甲\n>\n> 乙").blocks.single() as Block.Quote
        assertEquals(listOf("b0.0", "b0.1"), quote.blocks.map { it.id })
    }

    @Test
    fun `flags 汇总 has_code has_table has_math 与 max_depth`() {
        val model = parser.parse("> > 深\n\n| a | b |\n| --- | --- |\n| 1 | 2 |\n\n\$x\$\n\n```js\n1\n```")
        assertTrue(model.flags.hasTable)
        assertTrue(model.flags.hasMath)
        assertTrue(model.flags.hasCode)
        assertEquals(2, model.flags.maxDepth)
        assertFalse(model.flags.truncated)
    }

    @Test
    fun `limits 超过总字符上限时截断并置 truncated`() {
        val tiny = MarkdownParser(RenderLimits(maxChars = 8))
        val model = tiny.parse("aaaaaaaaaaaaaaaaaaaa\n\nbbbb")
        assertTrue(model.flags.truncated)
        assertEquals(1, model.blocks.size)
    }

    @Test
    fun `limits 表格列数上限丢弃多余列`() {
        val narrow = MarkdownParser(RenderLimits(maxTableColumns = 2))
        val block = narrow.parse("| a | b | c |\n| - | - | - |\n| 1 | 2 | 3 |").blocks.single() as Block.Table
        assertEquals(2, block.header.size)
        assertEquals(2, block.rows.single().size)
    }

    @Test
    fun `limits 内联嵌套超过上限降级为 text`() {
        val shallow = MarkdownParser(RenderLimits(maxInlineDepth = 1))
        val block = shallow.parse("**粗 *斜* 粗**").blocks.single() as Block.Paragraph
        val strong = block.inlines.single() as Inline.Strong
        assertEquals(listOf(Inline.Text("粗 *斜* 粗")), strong.inlines)
    }

    @Test
    fun `畸形输入不抛异常`() {
        val odd = listOf("", "```", "> ", "|", "[](", "**", "$$", "\u0000\n\t-", "[^]: x", "***\n", "1.")
        for (case in odd) {
            val model = parser.parse(case, incomplete = true)
            assertTrue(model.blocks.size <= 2, "输入 [$case] 产出 ${model.blocks.size} 块")
        }
    }

    @Test
    fun `crlf 输入按行切开`() {
        val blocks = parser.parse("一\r\n\r\n二").blocks
        assertEquals(2, blocks.size)
        assertEquals(listOf(Inline.Text("一")), (blocks[0] as Block.Paragraph).inlines)
    }
}
