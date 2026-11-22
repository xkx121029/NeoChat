using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NeoChat.Protocol.Canonical;
using NeoChat.Protocol.Events;
using NeoChat.Windows.Core.Chat;
using NeoChat.Windows.Core.Errors;
using NeoChat.Windows.Core.Storage;

namespace NeoChat.Persistence.Probe;

/// <summary>
/// The scripted evidence harness for the persistence acceptance: it drives the real
/// <see cref="ChatSessionViewModel"/> and <see cref="SqliteConversationRepository"/> against a database
/// file chosen on the command line, with a local scripted stream so no provider or network is involved.
/// Each mode is a separate process, which is what makes the restart and kill proofs real.
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions SnapshotJson = new() { WriteIndented = true };

    private static readonly ProviderSettings Settings = new()
    {
        BaseUrl = "https://example.invalid/v1",
        ApiKey = "probe-not-a-secret",
        Model = "probe-model",
    };

    private static async Task<int> Main(string[] args)
    {
        // The console here is a Windows one (code page 936); the probe always speaks UTF-8, so the
        // evidence pasted into a report shows the real text instead of replacement characters.
        Console.OutputEncoding = new UTF8Encoding(false);

        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: <home|seed|read|hold|status|schema> [db-path] [json-path]");
            return 2;
        }

        var mode = args[0];
        if (string.Equals(mode, "home", StringComparison.Ordinal))
        {
            // The path the app itself uses: %LOCALAPPDATA%/NeoChat/neochat.db, with no arguments.
            return Home();
        }

        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: <home|seed|read|hold|status|schema> [db-path] [json-path]");
            return 2;
        }

        var databasePath = Path.GetFullPath(args[1]);
        return mode switch
        {
            "seed" => await SeedAsync(databasePath).ConfigureAwait(false),
            "read" => Read(databasePath),
            "hold" => await HoldAsync(databasePath).ConfigureAwait(false),
            "status" => Status(databasePath),
            "schema" => DumpSchema(databasePath, args.Length > 2 ? args[2] : null),
            _ => Unknown(mode),
        };
    }

    /// <summary>
    /// Opens the store the way <c>ChatPage</c> opens it at launch (default path, no arguments), so the
    /// evidence shows the real location rather than a temp directory.
    /// </summary>
    private static int Home()
    {
        using var database = NeoChatDatabase.Open();
        using var repository = new SqliteConversationRepository(database);
        Console.WriteLine("database=" + repository.DatabasePath);
        Console.WriteLine("schema-created=" + database.Created);
        Console.WriteLine("startup-swept-streaming=" + repository.AbortOrphanStreaming().ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("conversation-count=" + repository.ListConversations().Count.ToString(CultureInfo.InvariantCulture));
        PrintDatabase(repository.DatabasePath);
        return 0;
    }

    /// <summary>One whole turn through the production path, then exit.</summary>
    private static async Task<int> SeedAsync(string databasePath)
    {
        using var database = NeoChatDatabase.Open(databasePath);
        using var repository = new SqliteConversationRepository(database);
        var aborted = repository.AbortOrphanStreaming();
        Console.WriteLine("startup-swept-streaming=" + aborted.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("database=" + repository.DatabasePath);
        Console.WriteLine("schema-created=" + database.Created);

        var client = new ScriptedStreamClient()
            .Then(
                new StreamEvent { Kind = StreamEventKind.ReasoningDelta, Seq = 1, Text = "先想一下。" },
                new StreamEvent { Kind = StreamEventKind.Delta, Seq = 2, Text = "你好，" },
                new StreamEvent { Kind = StreamEventKind.Delta, Seq = 3, Text = "这是重启验收用的回复。" },
                new StreamEvent { Kind = StreamEventKind.Done, Seq = 4, FinishReason = FinishReason.Stop });
        using var viewModel = new ChatSessionViewModel(client, new ProbeFailureLog(), Settings, repository);
        viewModel.OpenConversation(repository.EnsureOpenConversation().Id);

        viewModel.ComposerText = "重启后还认得这句话吗";
        await viewModel.SendAsync().ConfigureAwait(false);

        Console.WriteLine("conversation-id=" + viewModel.ConversationId);
        Console.WriteLine("session-title=" + viewModel.SessionTitle);
        Console.WriteLine("session-status=" + viewModel.StatusText);
        PrintDatabase(databasePath);
        return 0;
    }

    /// <summary>A second process opens the file that the first one left behind.</summary>
    private static int Read(string databasePath)
    {
        using var database = NeoChatDatabase.Open(databasePath);
        using var repository = new SqliteConversationRepository(database);
        Console.WriteLine("schema-created=" + database.Created);
        Console.WriteLine("startup-swept-streaming=" + repository.AbortOrphanStreaming().ToString(CultureInfo.InvariantCulture));

        var conversations = repository.ListConversations();
        Console.WriteLine("conversation-count=" + conversations.Count.ToString(CultureInfo.InvariantCulture));
        if (conversations.Count == 0)
        {
            return 1;
        }

        var newest = conversations[0];
        var messages = repository.LoadThread(newest.Id);
        Console.WriteLine("newest-conversation-id=" + newest.Id);
        Console.WriteLine("newest-conversation-title=" + newest.DisplayTitle);
        Console.WriteLine("message-count=" + messages.Count.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("first-message-role=" + messages[0].Role);
        Console.WriteLine("first-message-text=" + messages[0].Text);
        if (messages.Count > 1)
        {
            Console.WriteLine("second-message-status=" + messages[1].Status);
            Console.WriteLine("second-message-text=" + messages[1].Text);
            Console.WriteLine("second-message-reasoning=" + messages[1].ReasoningText);
        }

        PrintDatabase(databasePath);
        return messages.Count == 0 ? 1 : 0;
    }

    /// <summary>
    /// Streams forever without a terminal event. The acceptance script kills this process with
    /// <c>taskkill /F</c>; what reached the disk is whatever the following run of <c>status</c> shows.
    /// </summary>
    private static async Task<int> HoldAsync(string databasePath)
    {
        using var database = NeoChatDatabase.Open(databasePath);
        using var repository = new SqliteConversationRepository(database);
        repository.AbortOrphanStreaming();

        using var viewModel = new ChatSessionViewModel(
            new EndlessStreamClient(TimeSpan.FromMilliseconds(200)),
            new ProbeFailureLog(),
            Settings,
            repository);
        viewModel.OpenConversation(repository.EnsureOpenConversation().Id);
        viewModel.ComposerText = "崩溃前把已经生成的部分写进磁盘";
        _ = viewModel.SendAsync();

        await Task.Delay(900).ConfigureAwait(false);
        Console.WriteLine("streaming-now:");
        PrintDatabase(databasePath);
        Console.Out.Flush();
        await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
        return 0;
    }

    private static int Status(string databasePath)
    {
        using var database = NeoChatDatabase.Open(databasePath);
        Console.WriteLine("opened-without-migration=" + (!database.Created));
        PrintDatabase(databasePath);
        return 0;
    }

    /// <summary>
    /// Writes the database's own schema in the snapshot shape <c>tools/schema-diff/gen_baseline.py</c>
    /// produces, so the two can be compared column by column.
    /// </summary>
    private static int DumpSchema(string databasePath, string? jsonPath)
    {
        using var connection = Connect(databasePath);
        var tables = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var table in TableNames(connection))
        {
            var columns = new SortedDictionary<string, object>(StringComparer.Ordinal);
            using var info = connection.CreateCommand();
            info.CommandText = "PRAGMA table_info('" + table.Replace("'", "''", StringComparison.Ordinal) + "')";
            using var reader = info.ExecuteReader();
            while (reader.Read())
            {
                columns[reader.GetString(1)] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["type"] = reader.IsDBNull(2) ? string.Empty : reader.GetString(2).ToUpperInvariant(),
                    ["notnull"] = !reader.IsDBNull(3) && reader.GetInt32(3) != 0,
                    ["pk"] = !reader.IsDBNull(5) && reader.GetInt32(5) != 0,
                    ["default"] = reader.IsDBNull(4) ? null! : reader.GetString(4),
                };
            }

            tables[table] = new Dictionary<string, object>(StringComparer.Ordinal) { ["columns"] = columns };
        }

        var snapshot = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["format"] = "neochat-schema-snapshot/1",
            ["tables"] = tables,
        };
        var json = JsonSerializer.Serialize(snapshot, SnapshotJson);

        if (jsonPath is null)
        {
            Console.WriteLine(json);
            return 0;
        }

        File.WriteAllText(Path.GetFullPath(jsonPath), json + "\n", new UTF8Encoding(false));
        Console.WriteLine("wrote " + Path.GetFullPath(jsonPath) + ": tables=" +
            tables.Count.ToString(CultureInfo.InvariantCulture));
        return 0;
    }

    private static int Unknown(string mode)
    {
        Console.Error.WriteLine("unknown mode: " + mode);
        return 2;
    }

    private static void PrintDatabase(string databasePath)
    {
        using var connection = Connect(databasePath);
        Console.WriteLine("-- conversations");
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT id, title, COALESCE(last_message_at, -1), message_count, " +
                "COALESCE(active_leaf_message_id, '-') FROM conversation ORDER BY last_message_at DESC";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                Console.WriteLine(
                    "conversation " + reader.GetString(0) +
                    " title=\"" + reader.GetString(1) + "\"" +
                    " last_message_at=" + reader.GetInt64(2).ToString(CultureInfo.InvariantCulture) +
                    " messages=" + reader.GetInt32(3).ToString(CultureInfo.InvariantCulture) +
                    " active_leaf=" + reader.GetString(4));
            }
        }

        Console.WriteLine("-- messages");
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT m.id, m.role, m.status, COALESCE(m.finish_reason, '-'), m.sibling_index, " +
                "COALESCE(m.parent_id, '-'), COALESCE(p.body, '') " +
                "FROM message m LEFT JOIN message_part p ON p.message_id = m.id AND p.part_index = 0 " +
                "ORDER BY m.created_at, m.sibling_index";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                Console.WriteLine(
                    "message " + reader.GetString(0) +
                    " role=" + reader.GetString(1) +
                    " status=" + reader.GetString(2) +
                    " finish=" + reader.GetString(3) +
                    " sibling=" + reader.GetInt32(4).ToString(CultureInfo.InvariantCulture) +
                    " parent=" + reader.GetString(5) +
                    " text=\"" + reader.GetString(6) + "\"");
            }
        }
    }

    private static SqliteConnection Connect(string databasePath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static List<string> TableNames(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private sealed class ProbeFailureLog : IFailureLog
    {
        public string Location => "(probe)";

        public void Record(ProviderError error) =>
            Console.Error.WriteLine("failure-log: " + error.Code + " " + error.Stage);
    }

    /// <summary>A fixed event script per attempt, so a probe run is deterministic and offline.</summary>
    private sealed class ScriptedStreamClient : IChatStreamClient
    {
        private readonly Queue<StreamEvent[]> _scripts = new();

        public ScriptedStreamClient Then(params StreamEvent[] events)
        {
            _scripts.Enqueue(events);
            return this;
        }

        public async IAsyncEnumerable<StreamEvent> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            var script = _scripts.Count > 0 ? _scripts.Dequeue() : [];
            foreach (var streamEvent in script)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return streamEvent;
                await Task.Yield();
            }
        }
    }

    /// <summary>Never terminates: proves that fragments land while the reply is still arriving.</summary>
    private sealed class EndlessStreamClient(TimeSpan interval) : IChatStreamClient
    {
        public async IAsyncEnumerable<StreamEvent> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            for (var index = 1; ; index++)
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                yield return new StreamEvent
                {
                    Kind = StreamEventKind.Delta,
                    Seq = index,
                    Text = "第" + index.ToString(CultureInfo.InvariantCulture) + "段 ",
                };
            }
        }
    }
}
