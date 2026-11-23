using NeoChat.Windows.Core.Render;

namespace NeoChat.Windows.Core.Chat;

/// <summary>
/// What the message actions can pull out of one reply. It reads the same RenderModel the body draws,
/// so the copy keys offer exactly the code the reader sees, including code nested in a list or a quote.
/// </summary>
public static class MessageContent
{
    /// <summary>Fenced code blocks in reading order, source text as written.</summary>
    public static IReadOnlyList<string> CodeBlocks(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return [];
        }

        var blocks = new List<string>();
        Collect(RenderMarkdown.Parse(markdown).Blocks, blocks);
        return blocks;
    }

    private static void Collect(IEnumerable<Block> blocks, List<string> found)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case CodeBlock code:
                    found.Add(code.Text);
                    break;
                case ListBlock list:
                    foreach (var item in list.Items)
                    {
                        Collect(item.Blocks, found);
                    }

                    break;
                case QuoteBlock quote:
                    Collect(quote.Blocks, found);
                    break;
                case FootnoteDefBlock footnote:
                    Collect(footnote.Blocks, found);
                    break;
            }
        }
    }
}
