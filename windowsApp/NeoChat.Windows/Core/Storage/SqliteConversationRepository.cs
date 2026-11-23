using System.Globalization;
using Microsoft.Data.Sqlite;
using NeoChat.Windows.Core.Chat;

namespace NeoChat.Windows.Core.Storage;

/// <summary>
/// <see cref="IConversationRepository"/> over the frozen DDL, using Microsoft.Data.Sqlite directly (no
/// ORM). One connection and one lock: the app writes from the UI thread and the acceptance harness from
/// a console run, never both at once, and SQLite's own WAL handles the rest.
/// </summary>
/// <remarks>
/// Streaming contract: <see cref="AppendText"/> commits on every fragment, so a killed process keeps
/// everything it had received. The part row and its <c>message_fts</c> row are written in one
/// transaction, as the DDL's FTS maintenance clause requires.
/// </remarks>
public sealed class SqliteConversationRepository : IConversationRepository
{
    private const string SelectConversationColumns =
        "id, title, last_message_at, message_count, pinned, archived";

    private readonly NeoChatDatabase _database;
    private readonly ITextSegmenter _segmenter;
    private readonly object _gate = new();
    private bool _disposed;

    public SqliteConversationRepository(NeoChatDatabase database)
        : this(database, PassThroughSegmenter.Instance)
    {
    }

    public SqliteConversationRepository(NeoChatDatabase database, ITextSegmenter segmenter)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(segmenter);
        _database = database;
        _segmenter = segmenter;
    }

    public string DatabasePath => _database.Path;

    public int AbortOrphanStreaming()
    {
        lock (_gate)
        {
            return NeoChatDatabase.AbortOrphanStreaming(_database.Connection, NeoChatDatabase.NowMilliseconds());
        }
    }

    public ConversationSummary EnsureOpenConversation()
    {
        lock (_gate)
        {
            using var find = Command(
                "SELECT " + SelectConversationColumns + " FROM conversation " +
                "WHERE deleted_at IS NULL AND archived = 0 AND message_count = 0 AND title_auto = 1 " +
                "ORDER BY created_at DESC LIMIT 1");
            using (var reader = find.ExecuteReader())
            {
                if (reader.Read())
                {
                    return ReadConversation(reader);
                }
            }

            return CreateConversationCore(NeoChatDatabase.NowMilliseconds());
        }
    }

    public ConversationSummary CreateConversation()
    {
        lock (_gate)
        {
            return CreateConversationCore(NeoChatDatabase.NowMilliseconds());
        }
    }

    public IReadOnlyList<ConversationSummary> ListConversations()
    {
        lock (_gate)
        {
            using var command = Command(
                "SELECT " + SelectConversationColumns + " FROM conversation " +
                "WHERE deleted_at IS NULL AND archived = 0 " +
                "ORDER BY last_message_at DESC, created_at DESC");

            var rows = new List<ConversationSummary>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(ReadConversation(reader));
            }

            return rows;
        }
    }

    public ConversationSummary? FindConversation(string conversationId)
    {
        lock (_gate)
        {
            using var command = Command(
                "SELECT " + SelectConversationColumns + " FROM conversation WHERE id = $id AND deleted_at IS NULL");
            command.Parameters.AddWithValue("$id", conversationId);
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadConversation(reader) : null;
        }
    }

    public IReadOnlyList<MessageRow> LoadThread(string conversationId)
    {
        lock (_gate)
        {
            var leaf = ReadActiveLeaf(conversationId);
            var rows = new List<MessageRow>();
            using var command = leaf is null ? BranchFallbackCommand(conversationId) : BranchCommand(conversationId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(ReadMessage(reader));
            }

            return rows;
        }
    }

    public MessageRow AppendMessage(
        string conversationId,
        ChatRole role,
        string? parentId,
        string text,
        StoredMessageStatus status,
        string? autoTitle = null)
    {
        lock (_gate)
        {
            var now = NeoChatDatabase.NowMilliseconds();
            var id = Uuid7.NewId();
            var siblingIndex = CountSiblings(conversationId, parentId);

            using (var transaction = _database.Connection.BeginTransaction())
            {
                using (var insert = Command(
                    "INSERT INTO message(id, conversation_id, parent_id, role, sibling_index, status, finish_reason, " +
                    "error_json, provider_id, model_id, truncated, sync_version, rev, origin_device, created_at, updated_at) " +
                    "VALUES ($id, $conversation, $parent, $role, $sibling, $status, NULL, NULL, NULL, NULL, 0, 1, $rev, " +
                    "$device, $now, $now)",
                    transaction))
                {
                    insert.Parameters.AddWithValue("$id", id);
                    insert.Parameters.AddWithValue("$conversation", conversationId);
                    insert.Parameters.AddWithValue("$parent", (object?)parentId ?? DBNull.Value);
                    insert.Parameters.AddWithValue("$role", RoleWire(role));
                    insert.Parameters.AddWithValue("$sibling", siblingIndex);
                    insert.Parameters.AddWithValue("$status", StoredMessageStatusWire.ToWire(status));
                    insert.Parameters.AddWithValue("$rev", NeoChatDatabase.NewRevision());
                    insert.Parameters.AddWithValue("$device", _database.DeviceId);
                    insert.Parameters.AddWithValue("$now", now);
                    insert.ExecuteNonQuery();
                }

                if (text.Length > 0)
                {
                    WritePart(transaction, id, 0, "text", text, now);
                    WriteFts(transaction, id, conversationId, 0, text);
                }

                TouchConversation(transaction, conversationId, autoTitle, now);
                transaction.Commit();
            }

            return new MessageRow(
                id,
                parentId,
                siblingIndex,
                role,
                status,
                text,
                string.Empty,
                null,
                null,
                now);
        }
    }

    public void AppendText(string messageId, string conversationId, string delta, string fullText)
    {
        ArgumentNullException.ThrowIfNull(delta);
        ArgumentNullException.ThrowIfNull(fullText);
        if (delta.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            var now = NeoChatDatabase.NowMilliseconds();
            using var transaction = _database.Connection.BeginTransaction();
            AppendPart(transaction, messageId, 0, "text", delta, now);
            WriteFts(transaction, messageId, conversationId, 0, fullText);
            TouchMessage(transaction, messageId, now);
            transaction.Commit();
        }
    }

    public void AppendReasoning(string messageId, string delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        if (delta.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            var now = NeoChatDatabase.NowMilliseconds();
            using var transaction = _database.Connection.BeginTransaction();
            AppendPart(transaction, messageId, 1, "reasoning", delta, now);
            TouchMessage(transaction, messageId, now);
            transaction.Commit();
        }
    }

    public void SetMessageStatus(string messageId, StoredMessageStatus status, string? finishReason, string? errorJson)
    {
        lock (_gate)
        {
            using var command = Command(
                "UPDATE message SET status = $status, finish_reason = $reason, error_json = $error, updated_at = $now, " +
                "sync_version = sync_version + 1, rev = $rev WHERE id = $id");
            command.Parameters.AddWithValue("$status", StoredMessageStatusWire.ToWire(status));
            command.Parameters.AddWithValue("$reason", (object?)finishReason ?? DBNull.Value);
            command.Parameters.AddWithValue("$error", (object?)errorJson ?? DBNull.Value);
            command.Parameters.AddWithValue("$now", NeoChatDatabase.NowMilliseconds());
            command.Parameters.AddWithValue("$rev", NeoChatDatabase.NewRevision());
            command.Parameters.AddWithValue("$id", messageId);
            command.ExecuteNonQuery();
        }
    }

    public void SetActiveLeaf(string conversationId, string messageId)
    {
        lock (_gate)
        {
            using var command = Command(
                "UPDATE conversation SET active_leaf_message_id = $message, updated_at = $now, " +
                "sync_version = sync_version + 1, rev = $rev WHERE id = $id");
            command.Parameters.AddWithValue("$message", messageId);
            command.Parameters.AddWithValue("$now", NeoChatDatabase.NowMilliseconds());
            command.Parameters.AddWithValue("$rev", NeoChatDatabase.NewRevision());
            command.Parameters.AddWithValue("$id", conversationId);
            command.ExecuteNonQuery();
        }
    }

    public int SoftDeleteMessage(string messageId)
    {
        lock (_gate)
        {
            var target = ReadMessageLocation(messageId);
            if (target is null)
            {
                return 0;
            }

            var (conversationId, parentId) = target.Value;
            var now = NeoChatDatabase.NowMilliseconds();
            var ids = CollectSubtree(messageId);

            using (var transaction = _database.Connection.BeginTransaction())
            {
                foreach (var id in ids)
                {
                    using (var mark = Command(
                        "UPDATE message SET deleted_at = $now, updated_at = $now, sync_version = sync_version + 1, " +
                        "rev = $rev WHERE id = $id AND deleted_at IS NULL",
                        transaction))
                    {
                        mark.Parameters.AddWithValue("$now", now);
                        mark.Parameters.AddWithValue("$rev", NeoChatDatabase.NewRevision());
                        mark.Parameters.AddWithValue("$id", id);
                        mark.ExecuteNonQuery();
                    }

                    // FTS maintenance contract (v1.sql): only live messages are indexed.
                    using var fts = Command("DELETE FROM message_fts WHERE message_id = $id", transaction);
                    fts.Parameters.AddWithValue("$id", id);
                    fts.ExecuteNonQuery();
                }

                RetouchConversation(transaction, conversationId, now);
                transaction.Commit();
            }

            if (ReadActiveLeaf(conversationId) is { } leaf && ids.Contains(leaf, StringComparer.Ordinal))
            {
                PointActiveLeaf(conversationId, parentId);
            }

            return ids.Count;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _database.Dispose();
    }

    private ConversationSummary CreateConversationCore(long now)
    {
        // title stays empty with title_auto = 1: the first user message names it.
        var id = Uuid7.NewId();
        using var command = Command(
            "INSERT INTO conversation(id, title, title_auto, agent_id, folder_id, pinned, archived, " +
            "active_leaf_message_id, settings_json, last_message_at, message_count, sync_version, rev, " +
            "origin_device, created_at, updated_at) " +
            "VALUES ($id, '', 1, NULL, NULL, 0, 0, NULL, NULL, NULL, 0, 1, $rev, $device, $now, $now)");
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$rev", NeoChatDatabase.NewRevision());
        command.Parameters.AddWithValue("$device", _database.DeviceId);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();

        return new ConversationSummary(id, string.Empty, null, 0, false, false);
    }

    private int CountSiblings(string conversationId, string? parentId)
    {
        using var command = parentId is null
            ? Command(
                "SELECT COUNT(*) FROM message WHERE conversation_id = $conversation AND parent_id IS NULL AND deleted_at IS NULL")
            : Command(
                "SELECT COUNT(*) FROM message WHERE conversation_id = $conversation AND parent_id = $parent AND deleted_at IS NULL");
        command.Parameters.AddWithValue("$conversation", conversationId);
        if (parentId is not null)
        {
            command.Parameters.AddWithValue("$parent", parentId);
        }

        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private (string ConversationId, string? ParentId)? ReadMessageLocation(string messageId)
    {
        using var command = Command("SELECT conversation_id, parent_id FROM message WHERE id = $id");
        command.Parameters.AddWithValue("$id", messageId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    /// <summary>Message id plus every message hanging below it, live rows only.</summary>
    private List<string> CollectSubtree(string messageId)
    {
        using var command = Command(
            "WITH RECURSIVE subtree(id) AS (" +
            "  SELECT id FROM message WHERE id = $id AND deleted_at IS NULL " +
            "  UNION ALL " +
            "  SELECT m.id FROM message m JOIN subtree s ON m.parent_id = s.id WHERE m.deleted_at IS NULL" +
            ") SELECT id FROM subtree");
        command.Parameters.AddWithValue("$id", messageId);

        var ids = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    private void PointActiveLeaf(string conversationId, string? messageId)
    {
        using var command = Command(
            "UPDATE conversation SET active_leaf_message_id = $message, updated_at = $now, " +
            "sync_version = sync_version + 1, rev = $rev WHERE id = $id");
        command.Parameters.AddWithValue("$message", (object?)messageId ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", NeoChatDatabase.NowMilliseconds());
        command.Parameters.AddWithValue("$rev", NeoChatDatabase.NewRevision());
        command.Parameters.AddWithValue("$id", conversationId);
        command.ExecuteNonQuery();
    }

    /// <summary>Recounts <c>message_count</c> / <c>last_message_at</c> after rows left the live set.</summary>
    private void RetouchConversation(SqliteTransaction transaction, string conversationId, long now)
    {
        using var command = Command(
            "UPDATE conversation SET " +
            "  last_message_at = (SELECT MAX(created_at) FROM message WHERE conversation_id = $id AND deleted_at IS NULL), " +
            "  message_count = (SELECT COUNT(*) FROM message WHERE conversation_id = $id AND deleted_at IS NULL), " +
            "  updated_at = $now, sync_version = sync_version + 1, rev = $rev " +
            "WHERE id = $id",
            transaction);
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$rev", NeoChatDatabase.NewRevision());
        command.Parameters.AddWithValue("$id", conversationId);
        command.ExecuteNonQuery();
    }

    private string? ReadActiveLeaf(string conversationId)
    {
        using var command = Command("SELECT active_leaf_message_id FROM conversation WHERE id = $id");
        command.Parameters.AddWithValue("$id", conversationId);
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private SqliteCommand BranchCommand(string conversationId)
    {
        // Walk from the active leaf up to the root, then read downward so the result is root-first.
        var command = Command(
            "WITH RECURSIVE branch(id, depth) AS (" +
            "  SELECT id, 0 FROM message WHERE id = (SELECT active_leaf_message_id FROM conversation WHERE id = $conversation) " +
            "  UNION ALL " +
            "  SELECT m.parent_id, b.depth + 1 FROM message m JOIN branch b ON m.id = b.id WHERE m.parent_id IS NOT NULL" +
            ") " +
            "SELECT m.id, m.parent_id, m.sibling_index, m.role, m.status, m.finish_reason, m.error_json, m.created_at, " +
            "  COALESCE(tp.body, '') AS text_body, COALESCE(rp.body, '') AS reasoning_body " +
            "FROM message m " +
            "JOIN branch b ON b.id = m.id " +
            "LEFT JOIN message_part tp ON tp.message_id = m.id AND tp.part_index = 0 " +
            "LEFT JOIN message_part rp ON rp.message_id = m.id AND rp.part_index = 1 " +
            "WHERE m.deleted_at IS NULL " +
            "ORDER BY b.depth DESC, m.sibling_index ASC");
        command.Parameters.AddWithValue("$conversation", conversationId);
        return command;
    }

    private SqliteCommand BranchFallbackCommand(string conversationId)
    {
        // No active leaf yet: show every live message in insertion order.
        var command = Command(
            "SELECT m.id, m.parent_id, m.sibling_index, m.role, m.status, m.finish_reason, m.error_json, m.created_at, " +
            "  COALESCE(tp.body, '') AS text_body, COALESCE(rp.body, '') AS reasoning_body " +
            "FROM message m " +
            "LEFT JOIN message_part tp ON tp.message_id = m.id AND tp.part_index = 0 " +
            "LEFT JOIN message_part rp ON rp.message_id = m.id AND rp.part_index = 1 " +
            "WHERE m.conversation_id = $conversation AND m.deleted_at IS NULL " +
            "ORDER BY m.created_at ASC, m.sibling_index ASC");
        command.Parameters.AddWithValue("$conversation", conversationId);
        return command;
    }

    private void AppendPart(SqliteTransaction transaction, string messageId, int partIndex, string kind, string delta, long now)
    {
        using var command = Command(
            "INSERT INTO message_part(id, message_id, part_index, kind, body, meta_json, truncated, created_at, updated_at) " +
            "VALUES ($id, $message, $index, $kind, $body, NULL, 0, $now, $now) " +
            "ON CONFLICT(message_id, part_index) DO UPDATE SET body = message_part.body || excluded.body, " +
            "updated_at = excluded.updated_at",
            transaction);
        command.Parameters.AddWithValue("$id", Uuid7.NewId());
        command.Parameters.AddWithValue("$message", messageId);
        command.Parameters.AddWithValue("$index", partIndex);
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$body", delta);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();
    }

    private void WritePart(SqliteTransaction transaction, string messageId, int partIndex, string kind, string body, long now)
    {
        using var command = Command(
            "INSERT INTO message_part(id, message_id, part_index, kind, body, meta_json, truncated, created_at, updated_at) " +
            "VALUES ($id, $message, $index, $kind, $body, NULL, 0, $now, $now) " +
            "ON CONFLICT(message_id, part_index) DO UPDATE SET body = excluded.body, updated_at = excluded.updated_at",
            transaction);
        command.Parameters.AddWithValue("$id", Uuid7.NewId());
        command.Parameters.AddWithValue("$message", messageId);
        command.Parameters.AddWithValue("$index", partIndex);
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$body", body);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();
    }

    private void WriteFts(SqliteTransaction transaction, string messageId, string conversationId, int partIndex, string body)
    {
        using (var delete = Command("DELETE FROM message_fts WHERE message_id = $message AND part_index = $index", transaction))
        {
            delete.Parameters.AddWithValue("$message", messageId);
            delete.Parameters.AddWithValue("$index", partIndex);
            delete.ExecuteNonQuery();
        }

        if (body.Length == 0)
        {
            return;
        }

        using var insert = Command(
            "INSERT INTO message_fts(text, message_id, conversation_id, part_index) VALUES ($text, $message, $conversation, $index)",
            transaction);
        insert.Parameters.AddWithValue("$text", _segmenter.Segment(body));
        insert.Parameters.AddWithValue("$message", messageId);
        insert.Parameters.AddWithValue("$conversation", conversationId);
        insert.Parameters.AddWithValue("$index", partIndex);
        insert.ExecuteNonQuery();
    }

    private void TouchMessage(SqliteTransaction transaction, string messageId, long now)
    {
        using var command = Command(
            "UPDATE message SET updated_at = $now, sync_version = sync_version + 1, rev = $rev WHERE id = $id",
            transaction);
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$rev", NeoChatDatabase.NewRevision());
        command.Parameters.AddWithValue("$id", messageId);
        command.ExecuteNonQuery();
    }

    private void TouchConversation(SqliteTransaction transaction, string conversationId, string? autoTitle, long now)
    {
        using var command = Command(
            "UPDATE conversation SET " +
            "  title = CASE WHEN title_auto = 1 AND (title IS NULL OR title = '') AND $title IS NOT NULL " +
            "    THEN $title ELSE title END, " +
            "  last_message_at = (SELECT MAX(created_at) FROM message WHERE conversation_id = $id AND deleted_at IS NULL), " +
            "  message_count = (SELECT COUNT(*) FROM message WHERE conversation_id = $id AND deleted_at IS NULL), " +
            "  updated_at = $now, sync_version = sync_version + 1, rev = $rev " +
            "WHERE id = $id",
            transaction);
        command.Parameters.AddWithValue("$title", (object?)autoTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$rev", NeoChatDatabase.NewRevision());
        command.Parameters.AddWithValue("$id", conversationId);
        command.ExecuteNonQuery();
    }

    private SqliteCommand Command(string sql, SqliteTransaction? transaction = null)
    {
        var command = _database.Connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return command;
    }

    private static ConversationSummary ReadConversation(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetInt64(2),
        reader.GetInt32(3),
        reader.GetInt32(4) != 0,
        reader.GetInt32(5) != 0);

    /// <summary>
    /// Column order matches <see cref="BranchCommand"/>/<see cref="BranchFallbackCommand"/>:
    /// id, parent_id, sibling_index, role, status, finish_reason, error_json, created_at, text, reasoning.
    /// </summary>
    private static MessageRow ReadMessage(SqliteDataReader reader) => new(
        Id: reader.GetString(0),
        ParentId: reader.IsDBNull(1) ? null : reader.GetString(1),
        SiblingIndex: reader.GetInt32(2),
        Role: RoleFromWire(reader.GetString(3)),
        Status: StoredMessageStatusWire.FromWire(reader.GetString(4)),
        Text: reader.GetString(8) ?? string.Empty,
        ReasoningText: reader.GetString(9) ?? string.Empty,
        FinishReason: reader.IsDBNull(5) ? null : reader.GetString(5),
        ErrorJson: reader.IsDBNull(6) ? null : reader.GetString(6),
        CreatedAt: reader.GetInt64(7));

    private static string RoleWire(ChatRole role) => role switch
    {
        ChatRole.User => "user",
        ChatRole.Assistant => "assistant",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "unknown role"),
    };

    private static ChatRole RoleFromWire(string wire) => wire switch
    {
        "user" => ChatRole.User,
        "assistant" => ChatRole.Assistant,
        _ => throw new InvalidOperationException("M1 reloads only user and assistant rows, found: " + wire),
    };
}
