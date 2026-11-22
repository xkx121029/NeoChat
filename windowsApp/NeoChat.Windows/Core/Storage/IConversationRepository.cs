using NeoChat.Windows.Core.Chat;

namespace NeoChat.Windows.Core.Storage;

/// <summary>
/// Everything the chat layer needs from durable storage, expressed without any SQL so the view model
/// can be driven by an in-memory double in tests. Implemented by <see cref="SqliteConversationRepository"/>.
/// </summary>
public interface IConversationRepository : IDisposable
{
    /// <summary>Absolute path of the database file, for diagnostics and acceptance scripts.</summary>
    string DatabasePath { get; }

    /// <summary>
    /// Startup reconciliation: every message still marked <c>streaming</c> belongs to a process that died
    /// mid-reply. Returns how many rows were moved to <c>aborted</c>; their text is left as it was.
    /// </summary>
    int AbortOrphanStreaming();

    /// <summary>Opens the newest untouched conversation, or creates one, so "new chat" never piles up empties.</summary>
    ConversationSummary EnsureOpenConversation();

    ConversationSummary CreateConversation();

    /// <summary>Rail order: <c>last_message_at DESC</c>, so the conversation just spoken in comes first.</summary>
    IReadOnlyList<ConversationSummary> ListConversations();

    ConversationSummary? FindConversation(string conversationId);

    /// <summary>The active branch of a conversation, root first (follows <c>active_leaf_message_id</c>).</summary>
    IReadOnlyList<MessageRow> LoadThread(string conversationId);

    /// <summary>Inserts one message and refreshes the conversation's counters in the same transaction.</summary>
    MessageRow AppendMessage(
        string conversationId,
        ChatRole role,
        string? parentId,
        string text,
        StoredMessageStatus status,
        string? autoTitle = null);

    /// <summary>Appends one streamed text fragment and rewrites that part's full-text index row.</summary>
    void AppendText(string messageId, string conversationId, string delta, string fullText);

    /// <summary>Appends one streamed reasoning fragment. Reasoning is not full-text indexed.</summary>
    void AppendReasoning(string messageId, string delta);

    void SetMessageStatus(string messageId, StoredMessageStatus status, string? finishReason, string? errorJson);

    /// <summary>Points the conversation at the leaf the next turn continues from (message-tree cursor).</summary>
    void SetActiveLeaf(string conversationId, string messageId);
}

/// <summary>
/// The seam the DDL's FTS contract needs: <c>message_fts</c> indexes pre-segmented text because
/// <c>unicode61</c> does not split Chinese. Choosing the segmenter is ADR-004 work scheduled before M3,
/// so today the text is indexed as written and search is not surfaced anywhere in the UI.
/// </summary>
public interface ITextSegmenter
{
    string Segment(string text);
}

public sealed class PassThroughSegmenter : ITextSegmenter
{
    public static PassThroughSegmenter Instance { get; } = new();

    public string Segment(string text) => text;
}
