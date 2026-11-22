using System.Globalization;

namespace NeoChat.Windows.Core.Chat;

/// <summary>
/// The title rule for <c>conversation.title_auto = 1</c>: the conversation is named by its first user
/// message, collapsed to one line and cut at a whole grapheme so the rail never shows a broken glyph.
/// </summary>
public static class ConversationTitle
{
    /// <summary>Longest title kept before the ellipsis.</summary>
    public const int MaxTextElements = 24;

    public static string? FromFirstMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var words = text.Split(
            [' ', '\t', '\r', '\n', '\u3000'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var collapsed = string.Join(' ', words);
        if (collapsed.Length == 0)
        {
            return null;
        }

        var info = new StringInfo(collapsed);
        return info.LengthInTextElements <= MaxTextElements
            ? collapsed
            : info.SubstringByTextElements(0, MaxTextElements) + "…";
    }
}
