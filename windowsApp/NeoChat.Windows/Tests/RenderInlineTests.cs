using Xunit;
using NeoChat.Windows.Core.Render;

namespace NeoChat.Windows.Core.Tests;

/// <summary>§3 — one test per inline type, plus nesting depth and link safety.</summary>
public sealed class RenderInlineTests
{
    private static IReadOnlyList<Inline> Inlines(string markdown)
    {
        var document = RenderMarkdown.Parse(markdown);
        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));
        return paragraph.Inlines;
    }

    [Fact]
    public void Emph_And_Strong_And_Strike()
    {
        var inlines = Inlines("**粗** *斜* ~~删~~");

        Assert.IsType<StrongInline>(inlines[0]);
        Assert.IsType<TextInline>(inlines[1]);
        Assert.IsType<EmphInline>(inlines[2]);
        Assert.IsType<TextInline>(inlines[3]);
        Assert.IsType<StrikeInline>(inlines[4]);
    }

    [Fact]
    public void Strong_NestsEmph()
    {
        var inlines = Inlines("***both***");

        var strong = Assert.IsType<StrongInline>(Assert.Single(inlines));
        Assert.IsType<EmphInline>(Assert.Single(strong.Inlines));
    }

    [Fact]
    public void CodeSpan_TrimsOuterSpacesAndKeepsInner()
    {
        var inlines = Inlines("用 `a = b` 赋值");

        var code = Assert.IsType<CodeInline>(inlines[1]);
        Assert.Equal("a = b", code.Text);
    }

    [Fact]
    public void Link_KeepsDestTitleAndInnerFormatting()
    {
        var inlines = Inlines("[**粗**链接](https://example.com \"标题\")");

        var link = Assert.IsType<LinkInline>(Assert.Single(inlines));
        Assert.Equal("https://example.com", link.Dest);
        Assert.Equal("标题", link.Title);
        Assert.Equal(2, link.Inlines.Count);
        Assert.IsType<StrongInline>(link.Inlines[0]);
        Assert.Equal("链接", Assert.IsType<TextInline>(link.Inlines[1]).Text);
    }

    [Fact]
    public void InlineMath_DollarAndParenForms()
    {
        var dollar = Inlines("质能方程 $E = mc^2$ 成立");
        var paren = Inlines("质能方程 \\(E = mc^2\\) 成立");

        Assert.Equal("E = mc^2", Assert.IsType<MathInline>(dollar[1]).Tex);
        Assert.Equal("E = mc^2", Assert.IsType<MathInline>(paren[1]).Tex);
    }

    [Fact]
    public void UnclosedInlineMath_StaysLiteralText()
    {
        var inlines = Inlines("价格 $5 与 $6 无关");

        Assert.All(inlines, static inline => Assert.IsType<TextInline>(inline));
    }

    [Fact]
    public void HardBreak_FromTwoTrailingSpaces()
    {
        var inlines = Inlines("第一行  \n第二行");

        Assert.Equal("第一行", Assert.IsType<TextInline>(inlines[0]).Text);
        Assert.IsType<BrInline>(inlines[1]);
        Assert.Equal("第二行", Assert.IsType<TextInline>(inlines[2]).Text);
    }

    [Fact]
    public void BackslashEscape_ProducesLiteralCharacter()
    {
        var inlines = Inlines("\\*不是斜体\\*");

        Assert.Equal("*不是斜体*", Assert.IsType<TextInline>(Assert.Single(inlines)).Text);
    }

    [Fact]
    public void RawInlineHtml_IsLiteralTextNotMarkup()
    {
        var inlines = Inlines("a <b>bold</b> c");

        var text = string.Concat(inlines.Select(static inline => Assert.IsType<TextInline>(inline).Text));
        Assert.Equal("a <b>bold</b> c", text);
    }

    [Fact]
    public void Nesting_OverEightLevelsDegradesToText()
    {
        var markdown = new string('*', 0) + string.Concat(Enumerable.Repeat("**", RenderLimits.MaxInlineDepth + 2)) +
                       "深" +
                       string.Concat(Enumerable.Repeat("**", RenderLimits.MaxInlineDepth + 2));

        var document = RenderMarkdown.Parse(markdown);
        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));

        var depth = 0;
        var current = paragraph.Inlines;
        while (current.Count == 1 && current[0] is StrongInline strong)
        {
            depth++;
            current = strong.Inlines;
        }

        Assert.True(depth <= RenderLimits.MaxInlineDepth, $"nested {depth} levels");
        Assert.Contains(
            current.Concat(Flatten(paragraph.Inlines)),
            static inline => inline is TextInline text && text.Text.Contains('深'));
    }

    [Theory]
    [InlineData("http://example.com", true)]
    [InlineData("https://example.com", true)]
    [InlineData("mailto:a@b.cn", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html;base64,AAA", false)]
    [InlineData("file:///C:/x", false)]
    [InlineData("neochat://open", false)]
    public void LinkSafety_WhitelistsHttpHttpsMailto(string dest, bool allowed)
    {
        Assert.Equal(allowed, LinkSafety.IsAllowedLinkDestination(dest));
    }

    [Fact]
    public void ImageSource_AllowsHttpAndAttachmentOnly()
    {
        Assert.True(LinkSafety.IsAllowedImageSource("https://example.com/a.png"));
        Assert.True(LinkSafety.IsAllowedImageSource("attachment://abc"));
        Assert.False(LinkSafety.IsAllowedImageSource("file:///C:/a.png"));
    }

    private static IEnumerable<Inline> Flatten(IReadOnlyList<Inline> inlines)
    {
        foreach (var inline in inlines)
        {
            yield return inline;
            var children = inline switch
            {
                EmphInline emph => emph.Inlines,
                StrongInline strong => strong.Inlines,
                StrikeInline strike => strike.Inlines,
                LinkInline link => link.Inlines,
                _ => null,
            };
            if (children is null)
            {
                continue;
            }

            foreach (var child in Flatten(children))
            {
                yield return child;
            }
        }
    }
}
