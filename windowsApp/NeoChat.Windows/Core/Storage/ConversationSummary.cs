namespace NeoChat.Windows.Core.Storage;

/// <summary>One row of the conversation list: what the rail shows and how it is ordered.</summary>
public sealed record ConversationSummary(
    string Id,
    string Title,
    long? LastMessageAt,
    int MessageCount,
    bool Pinned,
    bool Archived)
{
    /// <summary>Untitled conversations still get a label in the rail (title_auto has not fired yet).</summary>
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "新对话" : Title;
}

/// <summary>
/// One message read back from the database, with the two part kinds M1 renders folded in.
/// <paramref name="ParentId"/> and <paramref name="SiblingIndex"/> are the message-tree fields the DDL
/// requires even though M1 has no branch switcher.
/// </summary>
public sealed record MessageRow(
    string Id,
    string? ParentId,
    int SiblingIndex,
    Chat.ChatRole Role,
    StoredMessageStatus Status,
    string Text,
    string ReasoningText,
    string? FinishReason,
    string? ErrorJson,
    long CreatedAt);
