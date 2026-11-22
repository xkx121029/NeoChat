using Xunit;
using NeoChat.Windows.Core.Render;

namespace NeoChat.Windows.Core.Tests;

/// <summary>§2 — one test per block type plus the structural rules the renderer depends on.</summary>
public sealed class RenderBlockTests
{
    private static readonly string[] ExpectedIds = ["b0", "b1", "b2"];

    private static RenderDocument Parse(string markdown) => RenderMarkdown.Parse(markdown);

    [Fact]
    public void Paragraph_CarriesTextInlines()
    {
        var document = Parse("你好，世界。");

        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));
        var text = Assert.IsType<TextInline>(Assert.Single(paragraph.Inlines));
        Assert.Equal("你好，世界。", text.Text);
        Assert.Equal("b0", paragraph.Id);
    }

    [Fact]
    public void Paragraph_SoftBreakBecomesSpace()
    {
        var document = Parse("第一行\n第二行");

        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));
        Assert.Equal("第一行 第二行", Assert.IsType<TextInline>(Assert.Single(paragraph.Inlines)).Text);
    }

    [Theory]
    [InlineData("# 一级", 1)]
    [InlineData("### 三级 ###", 3)]
    [InlineData("###### 六级", 6)]
    public void AtxHeading_KeepsLevel(string markdown, int level)
    {
        var document = Parse(markdown);

        var heading = Assert.IsType<HeadingBlock>(Assert.Single(document.Blocks));
        Assert.Equal(level, heading.Level);
    }

    [Fact]
    public void SetextHeading_LevelOneAndTwo()
    {
        var document = Parse("标题一\n===\n\n标题二\n---");

        Assert.Equal(2, document.Blocks.Count);
        Assert.Equal(1, Assert.IsType<HeadingBlock>(document.Blocks[0]).Level);
        Assert.Equal(2, Assert.IsType<HeadingBlock>(document.Blocks[1]).Level);
    }

    [Fact]
    public void CodeFence_KeepsLangAndText()
    {
        var document = Parse("```python\nprint(1)\n```");

        var code = Assert.IsType<CodeBlock>(Assert.Single(document.Blocks));
        Assert.Equal("python", code.Lang);
        Assert.Equal("print(1)", code.Text);
        Assert.True(code.Closed);
    }

    [Fact]
    public void CodeFence_ParsesInfoStringMeta()
    {
        var document = Parse("```csharp filename=Program.cs ln\nclass P { }\n```");

        var code = Assert.IsType<CodeBlock>(Assert.Single(document.Blocks));
        Assert.Equal("csharp", code.Lang);
        Assert.Equal("Program.cs", code.Meta.FileName);
        Assert.True(code.Meta.LineNumbers);
    }

    [Fact]
    public void CodeFence_UnparsableInfoStringStaysAsLang()
    {
        var document = Parse("```js {highlight}\nx\n```");

        var code = Assert.IsType<CodeBlock>(Assert.Single(document.Blocks));
        Assert.Equal("js {highlight}", code.Lang);
        Assert.Null(code.Meta.FileName);
    }

    [Fact]
    public void CodeFence_UnclosedIsClosedFalseAndKeepsGrowing()
    {
        var document = Parse("```python\nprint(0)\nprint(1)");

        var code = Assert.IsType<CodeBlock>(Assert.Single(document.Blocks));
        Assert.False(code.Closed);
        Assert.Equal("print(0)\nprint(1)", code.Text);
    }

    [Fact]
    public void MathBlock_ClosedAndUnclosed()
    {
        var closed = Parse("$$\nE = mc^2\n$$");
        var open = Parse("$$\nE = mc^2");

        Assert.True(Assert.IsType<MathBlock>(Assert.Single(closed.Blocks)).Closed);
        var unclosed = Assert.IsType<MathBlock>(Assert.Single(open.Blocks));
        Assert.False(unclosed.Closed);
        Assert.Equal("E = mc^2", unclosed.Tex);
        Assert.Equal("block", unclosed.Display);
    }

    [Fact]
    public void MathBlock_BracketForm()
    {
        var document = Parse("\\[\na^2 + b^2 = c^2\n\\]");

        var math = Assert.IsType<MathBlock>(Assert.Single(document.Blocks));
        Assert.True(math.Closed);
        Assert.Equal("a^2 + b^2 = c^2", math.Tex);
    }

    [Theory]
    [InlineData("```mermaid\nflowchart TD\n  A-->B\n```", MermaidKinds.Flowchart)]
    [InlineData("```mermaid\nsequenceDiagram\n  A->>B: hi\n```", MermaidKinds.Sequence)]
    [InlineData("```mermaid\ngantt\n  title x\n```", MermaidKinds.Other)]
    public void MermaidBlock_ClassifiesKind(string markdown, string kind)
    {
        var document = Parse(markdown);

        var mermaid = Assert.IsType<MermaidBlock>(Assert.Single(document.Blocks));
        Assert.Equal(kind, mermaid.Kind);
        Assert.True(mermaid.Closed);
    }

    [Fact]
    public void Table_AlignsAndEscapedPipeStaysInCell()
    {
        var document = Parse("| 左对齐 | 居中 | 右对齐 |\n| :--- | :---: | ---: |\n| `a\\|b` | 转义竖线 | 3 |");

        var table = Assert.IsType<TableBlock>(Assert.Single(document.Blocks));
        Assert.Equal(
            new string?[] { TableAlign.Left, TableAlign.Center, TableAlign.Right },
            table.Align);
        Assert.Equal(3, table.Header.Count);

        var firstCell = Assert.IsType<CodeInline>(Assert.Single(table.Rows[0][0]));
        Assert.Equal("a|b", firstCell.Text);
    }

    [Fact]
    public void List_UnorderedNestedAndOrdered()
    {
        var document = Parse("- 一\n  - 一a\n  - 一b\n- 二\n\n1. x\n2. y");

        Assert.Equal(2, document.Blocks.Count);
        var outer = Assert.IsType<ListBlock>(document.Blocks[0]);
        Assert.False(outer.Ordered);
        Assert.Equal(2, outer.Items.Count);
        // CommonMark: a nested list interrupts the item's first paragraph, so the item holds both.
        Assert.Equal(2, outer.Items[0].Blocks.Count);
        Assert.Equal("一", Assert.IsType<TextInline>(Assert.Single(Assert.IsType<ParagraphBlock>(outer.Items[0].Blocks[0]).Inlines)).Text);
        var nested = Assert.IsType<ListBlock>(outer.Items[0].Blocks[1]);
        Assert.Equal(2, nested.Items.Count);

        var ordered = Assert.IsType<ListBlock>(document.Blocks[1]);
        Assert.True(ordered.Ordered);
        Assert.Equal(1, ordered.Start);
        Assert.Equal(2, ordered.Items.Count);
    }

    [Fact]
    public void List_OrderedStartIsHonoured()
    {
        var document = Parse("3. 三\n4. 四");

        var list = Assert.IsType<ListBlock>(Assert.Single(document.Blocks));
        Assert.True(list.Ordered);
        Assert.Equal(3, list.Start);
    }

    [Fact]
    public void List_TaskCheckboxes()
    {
        var document = Parse("- [ ] 未完成\n- [x] 已完成\n- 普通项");

        var list = Assert.IsType<ListBlock>(Assert.Single(document.Blocks));
        Assert.Equal(3, list.Items.Count);
        Assert.False(list.Items[0].Checked);
        Assert.True(list.Items[1].Checked);
        Assert.Null(list.Items[2].Checked);
    }

    [Fact]
    public void List_LooseWhenBlankLineSeparatesItems()
    {
        var document = Parse("- a\n\n- b");

        var list = Assert.IsType<ListBlock>(Assert.Single(document.Blocks));
        Assert.False(list.Tight);
        Assert.Equal(2, list.Items.Count);
    }

    [Fact]
    public void Quote_NestsParagraphs()
    {
        var document = Parse("> 引用第一行\n> 引用第二行\n>\n> > 更深一层");

        var quote = Assert.IsType<QuoteBlock>(Assert.Single(document.Blocks));
        Assert.Equal(2, quote.Blocks.Count);
        Assert.IsType<ParagraphBlock>(quote.Blocks[0]);
        Assert.IsType<QuoteBlock>(quote.Blocks[1]);
    }

    [Fact]
    public void Hr_ThreeForms()
    {
        foreach (var markdown in new[] { "---", "***", "___" })
        {
            Assert.IsType<HrBlock>(Assert.Single(Parse(markdown).Blocks));
        }
    }

    [Fact]
    public void HtmlBlock_KeptAsLiteralSource()
    {
        var document = Parse("<div onclick=\"alert(1)\">hi</div>");

        var html = Assert.IsType<HtmlBlock>(Assert.Single(document.Blocks));
        Assert.Equal("<div onclick=\"alert(1)\">hi</div>", html.Source);
    }

    [Fact]
    public void FootnoteDefinition_AndReferencePair()
    {
        var document = Parse("正文里有脚注[^1]。\n\n[^1]: 脚注内容");

        Assert.Equal(2, document.Blocks.Count);
        var paragraph = Assert.IsType<ParagraphBlock>(document.Blocks[0]);
        Assert.Contains(paragraph.Inlines, static inline => inline is FootnoteRefInline);
        var footnote = Assert.IsType<FootnoteDefBlock>(document.Blocks[1]);
        Assert.Equal("1", footnote.Label);
        Assert.True(document.Flags.HasFootnote);
    }

    [Fact]
    public void Image_AloneOnALineIsBlockOtherwiseInline()
    {
        var block = Parse("![图](/a.png)");
        var inline = Parse("前文 ![图](/a.png) 后文");

        var image = Assert.IsType<ImageBlock>(Assert.Single(block.Blocks));
        Assert.Equal("/a.png", image.Src);
        Assert.Equal("图", image.Alt);

        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(inline.Blocks));
        Assert.Equal(3, paragraph.Inlines.Count);
        Assert.IsType<ImageInline>(paragraph.Inlines[1]);
    }

    [Fact]
    public void Unknown_ReferenceLinkDefinitionIsForwardCompatible()
    {
        var document = Parse("[文档][d]\n\n[d]: https://example.com");

        Assert.Equal(2, document.Blocks.Count);
        var paragraph = Assert.IsType<ParagraphBlock>(document.Blocks[0]);
        Assert.Equal("[文档][d]", Assert.IsType<TextInline>(Assert.Single(paragraph.Inlines)).Text);
        var unknown = Assert.IsType<UnknownBlock>(document.Blocks[1]);
        Assert.Equal("link_definition", unknown.Kind);
    }

    [Fact]
    public void IndentedCode_BecomesCodeBlock()
    {
        var document = Parse("    var x = 1;\n    var y = 2;");

        var code = Assert.IsType<CodeBlock>(Assert.Single(document.Blocks));
        Assert.Null(code.Lang);
        Assert.Equal("var x = 1;\nvar y = 2;", code.Text);
    }

    [Fact]
    public void Flags_ReportContentPresenceAndDepth()
    {
        var document = Parse("# 标题\n\n- a\n  - b\n\n| a | b |\n| --- | --- |\n| 1 | 2 |\n\n```js\nx\n```\n\n$e$");

        Assert.True(document.Flags.HasList);
        Assert.True(document.Flags.HasTable);
        Assert.True(document.Flags.HasCode);
        Assert.True(document.Flags.HasMath);
        Assert.False(document.Flags.Incomplete);
        Assert.False(document.Flags.Truncated);
        // §1 未定义 max_depth 的基数：本实现顶层块 = 0，每进一层容器 +1，故「列表项里的嵌套列表」最深为 2。
        Assert.Equal(2, document.Flags.MaxDepth);
    }

    [Fact]
    public void Blocks_ReceiveSequentialStableIds()
    {
        var document = Parse("# a\n\nb\n\n```\nc\n```");

        Assert.Equal(3, document.Blocks.Count);
        Assert.Equal(ExpectedIds, document.Blocks.Select(static block => block.Id));
    }
}
