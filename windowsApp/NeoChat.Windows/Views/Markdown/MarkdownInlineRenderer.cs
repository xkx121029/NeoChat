using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using NeoChat.Windows.Core.Design;
using NeoChat.Windows.Core.Render;
using Windows.UI.Text;
using Inline = NeoChat.Windows.Core.Render.Inline;

namespace NeoChat.Windows.Views.Markdown;

/// <summary>
/// §3 inlines into a <see cref="RichTextBlock"/> paragraph. Raw HTML never reaches here as markup — the
/// parser already turned it into text, so everything below is data.
/// </summary>
internal static class MarkdownInlineRenderer
{
    private const string BlockedSuffix = "（已阻止）";

    internal static void Append(
        InlineCollection target,
        IReadOnlyList<Inline> inlines,
        bool strong = false,
        bool emph = false,
        bool strike = false)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case TextInline text:
                    target.Add(Styled(text.Text, strong, emph, strike));
                    break;
                case EmphInline nested:
                    Append(target, nested.Inlines, strong, !emph, strike);
                    break;
                case StrongInline nested:
                    Append(target, nested.Inlines, true, emph, strike);
                    break;
                case StrikeInline nested:
                    Append(target, nested.Inlines, strong, emph, true);
                    break;
                case CodeInline code:
                    target.Add(InlineCode(code.Text));
                    break;
                case LinkInline link:
                    AppendLink(target, link, strong, emph, strike);
                    break;
                case ImageInline image:
                    target.Add(InlineImage(image));
                    break;
                case MathInline math:
                    target.Add(Styled(math.Tex, strong, emph, strike, MarkdownLook.Mono));
                    break;
                case FootnoteRefInline footnote:
                    var badge = Styled("[" + footnote.Label + "]", strong, emph, strike, MarkdownLook.Mono);
                    badge.FontSize = Tokens.Type.MonoSmallSize;
                    badge.Foreground = MarkdownLook.InkMuted;
                    target.Add(badge);
                    break;
                case BrInline:
                    target.Add(new LineBreak());
                    break;
            }
        }
    }

    private static Run Styled(string text, bool strong, bool emph, bool strike, FontFamily? family = null)
    {
        var run = new Run { Text = text, FontFamily = family ?? MarkdownLook.Sans };
        if (strong)
        {
            run.FontWeight = FontWeights.SemiBold;
        }

        if (emph)
        {
            run.FontStyle = FontStyle.Italic;
        }

        if (strike)
        {
            run.TextDecorations = TextDecorations.Strikethrough;
        }

        return run;
    }

    private static InlineUIContainer InlineCode(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            FontFamily = MarkdownLook.Mono,
            FontSize = Tokens.Type.MonoSize,
            LineHeight = Tokens.Type.MonoLineHeight,
            Foreground = MarkdownLook.CodeWellFg,
            VerticalAlignment = VerticalAlignment.Center,
        };

        return new InlineUIContainer
        {
            Child = new Border
            {
                Background = MarkdownLook.CodeWellBg,
                CornerRadius = new CornerRadius(Tokens.Layout.InlineCodeRadius),
                Padding = new Thickness(
                    Tokens.Layout.InlineCodePaddingHorizontal,
                    Tokens.Layout.InlineCodePaddingVertical,
                    Tokens.Layout.InlineCodePaddingHorizontal,
                    Tokens.Layout.InlineCodePaddingVertical),
                Margin = new Thickness(Tokens.Space.Xxs, 0, Tokens.Space.Xxs, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = label,
            },
        };
    }

    private static void AppendLink(InlineCollection target, LinkInline link, bool strong, bool emph, bool strike)
    {
        if (!LinkSafety.IsAllowedLinkDestination(link.Dest))
        {
            // §7: a scheme outside the whitelist renders as plain, unclickable text plus a visible hint.
            Append(target, link.Inlines, strong, emph, strike);
            var hint = Styled(BlockedSuffix, false, false, false);
            hint.FontSize = Tokens.Type.MonoSmallSize;
            hint.Foreground = MarkdownLook.Danger;
            target.Add(hint);
            return;
        }

        var hyperlink = new Hyperlink
        {
            Foreground = MarkdownLook.Seed,
            UnderlineStyle = UnderlineStyle.None,
        };

        if (System.Uri.TryCreate(link.Dest, UriKind.Absolute, out var uri))
        {
            hyperlink.NavigateUri = uri;
        }

        Append(hyperlink.Inlines, link.Inlines, strong, emph, strike);
        target.Add(hyperlink);
    }

    private static InlineUIContainer InlineImage(ImageInline image)
    {
        var label = new TextBlock
        {
            Text = image.Alt.Length > 0 ? "[图片：" + image.Alt + "]" : "[图片]",
            FontFamily = MarkdownLook.Sans,
            FontSize = Tokens.Type.BodyMediumSize,
            Foreground = MarkdownLook.InkMuted,
            VerticalAlignment = VerticalAlignment.Center,
        };

        return new InlineUIContainer
        {
            Child = new Border
            {
                Background = MarkdownLook.CodeWellBg,
                CornerRadius = new CornerRadius(Tokens.Layout.InlineCodeRadius),
                Padding = new Thickness(
                    Tokens.Layout.InlineCodePaddingHorizontal,
                    Tokens.Layout.InlineCodePaddingVertical,
                    Tokens.Layout.InlineCodePaddingHorizontal,
                    Tokens.Layout.InlineCodePaddingVertical),
                Margin = new Thickness(Tokens.Space.Xxs, 0, Tokens.Space.Xxs, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = label,
            },
        };
    }
}
