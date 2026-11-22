using Xunit;
using System.Text;
using NeoChat.Windows.Core.Render;

namespace NeoChat.Windows.Core.Tests;

/// <summary>
/// The exact message used for the on-screen render check (M1-WIN-003). It pins the block sequence so a
/// rendering regression shows up as a parser failure rather than as a screenshot someone has to eyeball,
/// and it doubles as the L2 render manifest source (<c>render-manifest.json</c>: block types, line counts,
/// text SHA-256 per block).
/// </summary>
public sealed class RenderManifestTests
{
    private static readonly string[] ExpectedKinds =
    [
        "HeadingBlock", "ParagraphBlock", "HeadingBlock", "ListBlock", "ListBlock", "QuoteBlock",
        "CodeBlock", "TableBlock", "ParagraphBlock", "MathBlock", "ParagraphBlock", "FootnoteDefBlock",
    ];

    internal const string DemoMessage =
        "# 渲染验收：一条消息里放齐所有块\n\n" +
        "这条回复里有**粗体**、*斜体*、~~删除线~~、`inline code`，还有一个[链接](https://example.com)。行内公式 $E = mc^2$ 也在里面。\n\n" +
        "## 列表\n\n" +
        "1. 有序第一项\n2. 有序第二项\n   - 嵌套无序项\n   - 另一项\n\n" +
        "- [x] 任务列表已完成\n- [ ] 任务列表未完成\n\n" +
        "> 引用块：块内可以有段落，也可以有 `代码`。\n> > 再嵌一层。\n\n" +
        "```csharp filename=RenderDemo.cs ln\nvar block1 = Parse(segment1);\nvar block2 = Parse(segment2);\n```\n\n" +
        "| 左对齐 | 居中 | 右对齐 |\n| :--- | :---: | ---: |\n| `a\\|b` | 转义竖线不拆列 | 3 |\n| 无序 | 表格 | 42 |\n\n" +
        "块级公式：\n\n$$\n\\int_0^1 x^2 dx = \\frac{1}{3}\n$$\n\n" +
        "脚注也走一遍[^1]，最后补一句原文 HTML：<div onclick=\"x\">不执行</div>\n\n" +
        "[^1]: 这是脚注定义块。\n";

    [Fact]
    public void DemoMessage_ProducesEveryBlockKindItAdvertises()
    {
        var document = RenderMarkdown.Parse(DemoMessage);
        var kinds = document.Blocks.Select(static block => block.GetType().Name).ToArray();

        Assert.Equal(ExpectedKinds, kinds);

        Assert.True(document.Flags.HasCode);
        Assert.True(document.Flags.HasTable);
        Assert.True(document.Flags.HasList);
        Assert.True(document.Flags.HasMath);
        Assert.True(document.Flags.HasFootnote);
    }

    [Fact]
    public void DemoMessage_RenderManifestIsStable()
    {
        var manifest = RenderManifest.From(RenderMarkdown.Parse(DemoMessage));
        Assert.Contains("\"b0\":\"heading\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"b11\":\"footnote_def\"", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void DemoMessage_StreamsThroughTheSameFinalModel()
    {
        var parser = new IncrementalRenderParser();
        RenderDocument document = RenderMarkdown.Parse(string.Empty);
        for (var length = 1; length <= DemoMessage.Length; length++)
        {
            document = parser.Update(DemoMessage[..length]);
            _ = RenderModelJson.Serialize(document);
        }

        var final = parser.Finish();
        var oneShot = RenderMarkdown.Parse(DemoMessage);

        Assert.Equal(oneShot.Blocks.Count, final.Blocks.Count);
        for (var index = 0; index < oneShot.Blocks.Count; index++)
        {
            Assert.Equal(RenderModelJson.SerializeBlock(oneShot.Blocks[index]), RenderModelJson.SerializeBlock(final.Blocks[index]));
        }
    }
}

/// <summary>L2 manifest (§8): block type + line count + text hash per block, no pixels involved.</summary>
internal static class RenderManifest
{
    internal static string From(RenderDocument document)
    {
        var builder = new StringBuilder();
        builder.Append('{');
        for (var index = 0; index < document.Blocks.Count; index++)
        {
            var block = document.Blocks[index];
            if (index > 0)
            {
                builder.Append(',');
            }

            var text = BlockText(block);
            var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            builder.Append('"').Append(block.Id).Append("\":\"").Append(TypeName(block)).Append('"');
            builder.Append(",\"lines\":").Append(text.Split('\n').Length);
            builder.Append(",\"sha256\":\"").Append(hash).Append('"');
        }

        return builder.Append('}').ToString();
    }

    private static string TypeName(Block block) => block switch
    {
        ParagraphBlock => "paragraph",
        HeadingBlock => "heading",
        CodeBlock => "code",
        MathBlock => "math",
        MermaidBlock => "mermaid",
        TableBlock => "table",
        ListBlock => "list",
        QuoteBlock => "quote",
        HrBlock => "hr",
        HtmlBlock => "html",
        FootnoteDefBlock => "footnote_def",
        ImageBlock => "image",
        _ => "unknown",
    };

    private static string BlockText(Block block) => block switch
    {
        ParagraphBlock paragraph => PlainText(paragraph.Inlines),
        HeadingBlock heading => PlainText(heading.Inlines),
        CodeBlock code => code.Text,
        MathBlock math => math.Tex,
        HtmlBlock html => html.Source,
        UnknownBlock unknown => unknown.Raw,
        TableBlock table => string.Join('\n', table.Header.Select(PlainText).Concat(table.Rows.Select(static row => string.Join(" | ", row.Select(PlainText))))),
        ListBlock list => string.Join('\n', list.Items.SelectMany(static item => item.Blocks).Select(BlockText)),
        QuoteBlock quote => string.Join('\n', quote.Blocks.Select(BlockText)),
        FootnoteDefBlock footnote => footnote.Label + string.Join('\n', footnote.Blocks.Select(BlockText)),
        _ => string.Empty,
    };

    private static string PlainText(IReadOnlyList<Inline> inlines)
    {
        var builder = new StringBuilder();
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case TextInline text:
                    builder.Append(text.Text);
                    break;
                case CodeInline code:
                    builder.Append(code.Text);
                    break;
                case MathInline math:
                    builder.Append(math.Tex);
                    break;
                case EmphInline emph:
                    builder.Append(PlainText(emph.Inlines));
                    break;
                case StrongInline strong:
                    builder.Append(PlainText(strong.Inlines));
                    break;
                case StrikeInline strike:
                    builder.Append(PlainText(strike.Inlines));
                    break;
                case LinkInline link:
                    builder.Append(PlainText(link.Inlines));
                    break;
                case FootnoteRefInline footnote:
                    builder.Append('[').Append(footnote.Label).Append(']');
                    break;
                case ImageInline image:
                    builder.Append(image.Alt);
                    break;
            }
        }

        return builder.ToString();
    }
}
