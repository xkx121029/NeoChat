using System.Globalization;
using Microsoft.Data.Sqlite;
using NeoChat.Windows.Core.Chat;
using NeoChat.Windows.Core.Storage;
using Xunit;

namespace NeoChat.Windows.Core.Tests;

public sealed class ConversationPersistenceTests
{
    private static ProviderSettings Configured => new()
    {
        BaseUrl = "https://example.invalid/v1",
        ApiKey = "test-key-not-a-secret",
        Model = "test-model",
    };

    [Fact]
    public void FreshDatabase_ExecutesFrozenDdlAndRecordsTheSchemaVersion()
    {
        using var fixture = new DatabaseFixture();
        using var database = fixture.Open();

        Assert.True(database.Created);
        Assert.Equal("1", fixture.Scalar("SELECT value FROM schema_meta WHERE key = 'schema_version'"));
        Assert.Equal("1", fixture.Scalar("SELECT COUNT(*) FROM migration WHERE name = 'v1.sql' AND integrity_ok = 1"));
        // 22 tables in spec/schema/v1.sql, plus the FTS5 virtual table and its five shadow tables.
        Assert.Equal(
            "22",
            fixture.Scalar(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' " +
                "AND name NOT IN ('message_fts', 'message_fts_data', 'message_fts_idx', " +
                "'message_fts_content', 'message_fts_docsize', 'message_fts_config')"));
        Assert.Equal("1", fixture.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name = 'message_fts'"));
    }

    [Fact]
    public void ReopeningAnExistingDatabase_SkipsTheScript()
    {
        using var fixture = new DatabaseFixture();

        using (var first = fixture.Open())
        {
            Assert.True(first.Created);
        }

        using (var second = fixture.Open())
        {
            Assert.False(second.Created);
        }

        Assert.Equal("1", fixture.Scalar("SELECT COUNT(*) FROM migration"));
        Assert.Equal("1", fixture.Scalar("SELECT value FROM schema_meta WHERE key = 'schema_version'"));
    }

    [Fact]
    public async Task SendingATurn_WritesBothRowsWithTreeFieldsAndTerminalStatus()
    {
        using var fixture = new DatabaseFixture();
        using var repository = fixture.OpenRepository();
        var client = new ScriptedChatStreamClient()
            .Then(Events.Delta("你", 1), Events.Delta("好", 2), Events.Done(3));
        using var viewModel = new ChatSessionViewModel(client, new InMemoryFailureLog(), Configured, repository);
        viewModel.OpenConversation(repository.EnsureOpenConversation().Id);

        viewModel.ComposerText = "在吗";
        await viewModel.SendAsync();

        var rows = viewModel.Messages;
        Assert.Equal(2, rows.Count);
        Assert.NotNull(rows[0].RowId);
        Assert.NotNull(rows[1].RowId);
        Assert.Equal(rows[0].RowId, fixture.Scalar("SELECT parent_id FROM message WHERE id = '" + rows[1].RowId + "'"));
        Assert.Equal("0", fixture.Scalar("SELECT sibling_index FROM message WHERE id = '" + rows[1].RowId + "'"));
        Assert.Equal("complete", fixture.Scalar("SELECT status FROM message WHERE id = '" + rows[1].RowId + "'"));
        Assert.Equal("stop", fixture.Scalar("SELECT finish_reason FROM message WHERE id = '" + rows[1].RowId + "'"));
        Assert.Equal("你好", fixture.Scalar("SELECT body FROM message_part WHERE message_id = '" + rows[1].RowId + "' AND part_index = 0"));
        Assert.Equal(rows[1].RowId, fixture.Scalar(
            "SELECT active_leaf_message_id FROM conversation WHERE id = '" + viewModel.ConversationId + "'"));
    }

    [Fact]
    public async Task EveryStreamedFragmentIsOnDiskBeforeTheTurnEnds()
    {
        using var fixture = new DatabaseFixture();
        using var repository = fixture.OpenRepository();
        var client = new ScriptedChatStreamClient()
            .ThenWaitForCancel(out var delivered, Events.Delta("第一段", 1), Events.Delta("第二段", 2));
        using var viewModel = new ChatSessionViewModel(client, new InMemoryFailureLog(), Configured, repository);
        viewModel.OpenConversation(repository.EnsureOpenConversation().Id);

        viewModel.ComposerText = "写一段";
        var send = viewModel.SendAsync();
        await delivered();

        var assistantId = viewModel.Messages[1].RowId;
        Assert.NotNull(assistantId);
        Assert.Equal("streaming", fixture.Scalar("SELECT status FROM message WHERE id = '" + assistantId + "'"));
        Assert.Equal("第一段第二段", fixture.Scalar(
            "SELECT body FROM message_part WHERE message_id = '" + assistantId + "' AND part_index = 0"));
        Assert.Equal("第一段第二段", fixture.Scalar(
            "SELECT text FROM message_fts WHERE message_id = '" + assistantId + "'"));

        viewModel.Stop();
        await send;

        Assert.Equal("aborted", fixture.Scalar("SELECT status FROM message WHERE id = '" + assistantId + "'"));
        Assert.Equal("第一段第二段", fixture.Scalar(
            "SELECT body FROM message_part WHERE message_id = '" + assistantId + "' AND part_index = 0"));
    }

    [Fact]
    public void AProcessThatDiedMidStream_IsReconciledToAbortedAndKeepsItsText()
    {
        using var fixture = new DatabaseFixture();
        string conversationId;
        string assistantId;

        using (var repository = fixture.OpenRepository())
        {
            conversationId = repository.EnsureOpenConversation().Id;
            var user = repository.AppendMessage(conversationId, ChatRole.User, null, "问一句", StoredMessageStatus.Complete);
            assistantId = repository.AppendMessage(
                conversationId, ChatRole.Assistant, user.Id, string.Empty, StoredMessageStatus.Streaming).Id;
            repository.AppendText(assistantId, conversationId, "只写了一半", "只写了一半");
            // No SetMessageStatus: this is exactly the state a taskkill leaves behind.
        }

        using var reopened = new SqliteConversationRepository(fixture.Open());
        Assert.Equal(1, reopened.AbortOrphanStreaming());

        Assert.Equal("aborted", fixture.Scalar("SELECT status FROM message WHERE id = '" + assistantId + "'"));
        Assert.Equal("只写了一半", fixture.Scalar(
            "SELECT body FROM message_part WHERE message_id = '" + assistantId + "' AND part_index = 0"));

        var restored = reopened.LoadThread(conversationId);
        Assert.Equal(2, restored.Count);
        Assert.Equal(StoredMessageStatus.Aborted, restored[1].Status);
        Assert.Equal("只写了一半", restored[1].Text);
        Assert.Equal(0, reopened.AbortOrphanStreaming());
    }

    [Fact]
    public void TheRailIsOrderedByLastMessageTimeDescending()
    {
        using var fixture = new DatabaseFixture();
        using var repository = fixture.OpenRepository();

        var older = repository.CreateConversation();
        repository.AppendMessage(older.Id, ChatRole.User, null, "旧会话", StoredMessageStatus.Complete);
        var newer = repository.CreateConversation();
        repository.AppendMessage(newer.Id, ChatRole.User, null, "新会话", StoredMessageStatus.Complete, autoTitle: "新会话");
        var untouched = repository.CreateConversation();

        // Pin the timestamps instead of racing the clock: order must come from last_message_at, not insert order.
        fixture.Execute("UPDATE conversation SET last_message_at = 1000 WHERE id = '" + older.Id + "'");
        fixture.Execute("UPDATE conversation SET last_message_at = 2000 WHERE id = '" + newer.Id + "'");

        var listed = repository.ListConversations();
        Assert.Equal([newer.Id, older.Id, untouched.Id], listed.Select(entry => entry.Id));
        Assert.Null(listed[2].LastMessageAt);
        Assert.Equal("新会话", listed[0].Title);
    }

    [Fact]
    public async Task RetryingAFailedReply_AddsASiblingAndTheThreadFollowsTheActiveLeaf()
    {
        using var fixture = new DatabaseFixture();
        using var repository = fixture.OpenRepository();
        var client = new ScriptedChatStreamClient()
            .ThenThrow(new HttpRequestException("connection reset"))
            .Then(Events.Delta("重来一次", 1), Events.Done(2));
        using var viewModel = new ChatSessionViewModel(client, new InMemoryFailureLog(), Configured, repository);
        viewModel.OpenConversation(repository.EnsureOpenConversation().Id);

        viewModel.ComposerText = "问一句";
        await viewModel.SendAsync();
        Assert.True(viewModel.HasError);

        await viewModel.RetryAsync();

        Assert.Equal(2, viewModel.Messages.Count);
        Assert.Equal("重来一次", viewModel.Messages[1].Text);

        var conversationId = Assert.IsType<string>(viewModel.ConversationId);
        Assert.Equal("3", fixture.Scalar("SELECT COUNT(*) FROM message WHERE conversation_id = '" + conversationId + "'"));
        Assert.Equal("1", fixture.Scalar(
            "SELECT COUNT(*) FROM message WHERE conversation_id = '" + conversationId + "' AND sibling_index = 1"));

        // A reload shows one branch only: the leaf the retry produced.
        var reloaded = repository.LoadThread(conversationId);
        Assert.Equal(2, reloaded.Count);
        Assert.Equal("重来一次", reloaded[1].Text);
        Assert.Equal(StoredMessageStatus.Complete, reloaded[1].Status);
    }

    [Fact]
    public async Task ReopeningTheDatabaseRestoresTheConversationForTheUi()
    {
        using var fixture = new DatabaseFixture();
        string conversationId;

        using (var repository = fixture.OpenRepository())
        {
            var client = new ScriptedChatStreamClient()
                .Then(Events.Delta("重启", 1), Events.Delta("还在", 2), Events.Done(3));
            using var viewModel = new ChatSessionViewModel(client, new InMemoryFailureLog(), Configured, repository);
            conversationId = repository.EnsureOpenConversation().Id;
            viewModel.OpenConversation(conversationId);
            viewModel.ComposerText = "记住这句话";
            await viewModel.SendAsync();
        }

        using var reopened = new SqliteConversationRepository(fixture.Open());
        var listed = reopened.ListConversations();
        Assert.Single(listed);
        Assert.Equal("记住这句话", listed[0].Title);
        Assert.Equal(2, listed[0].MessageCount);

        var rows = reopened.LoadThread(conversationId);
        Assert.Equal(2, rows.Count);
        Assert.Equal("记住这句话", rows[0].Text);
        Assert.Equal("重启还在", rows[1].Text);
    }

    [Fact]
    public void NewChat_ReusesAnUntouchedConversationInsteadOfPilingUpRows()
    {
        using var fixture = new DatabaseFixture();
        using var repository = fixture.OpenRepository();
        using var viewModel = new ChatSessionViewModel(
            new ScriptedChatStreamClient(), new InMemoryFailureLog(), Configured, repository);

        viewModel.StartNewConversation();
        viewModel.StartNewConversation();
        viewModel.StartNewConversation();

        Assert.Equal("1", fixture.Scalar("SELECT COUNT(*) FROM conversation"));
        Assert.Equal(ChatSessionViewModel.DefaultSessionTitle, viewModel.SessionTitle);
    }

    [Fact]
    public void TheRailShowsTheTitleTheTimeAndTheRepositoryOrder()
    {
        using var fixture = new DatabaseFixture();
        using var repository = fixture.OpenRepository();
        var older = repository.CreateConversation();
        repository.AppendMessage(older.Id, ChatRole.User, null, "旧会话", StoredMessageStatus.Complete, autoTitle: "旧会话");
        var newer = repository.CreateConversation();
        repository.AppendMessage(newer.Id, ChatRole.User, null, "新会话", StoredMessageStatus.Complete, autoTitle: "新会话");
        fixture.Execute("UPDATE conversation SET last_message_at = 1000 WHERE id = '" + older.Id + "'");
        var now = new DateTimeOffset(DateTime.Today.AddHours(12));
        fixture.Execute(
            "UPDATE conversation SET last_message_at = " +
            now.AddMinutes(-5).ToUnixTimeMilliseconds() + " WHERE id = '" + newer.Id + "'");

        var rail = new ConversationListViewModel(repository, new FixedTimeProvider(now));
        rail.Refresh();

        Assert.False(rail.IsEmpty);
        Assert.Equal(2, rail.Items.Count);
        Assert.Equal("新会话", rail.Items[0].Title);
        Assert.Equal(now.AddMinutes(-5).ToString("HH:mm", CultureInfo.CurrentCulture), rail.Items[0].TimeLabel);
        Assert.Equal("旧会话", rail.Items[1].Title);
        Assert.NotEqual("—", rail.Items[1].TimeLabel);

        rail.Select(newer.Id);
        rail.Refresh();
        Assert.Equal(newer.Id, rail.SelectedId);
    }

    [Fact]
    public void TheRailStartsEmptyAndSaysSo()
    {
        var rail = new ConversationListViewModel(null);

        Assert.True(rail.IsEmpty);
        Assert.Empty(rail.Items);

        rail.Refresh();
        Assert.True(rail.IsEmpty);
    }

    [Fact]
    public void ConversationTime_UsesClockYesterdayMonthDayThenFullDate()
    {
        var now = new DateTimeOffset(DateTime.Today.AddHours(12));
        var local = TimeZoneInfo.Local;

        var today = new DateTimeOffset(DateTime.Today.AddHours(9), local.GetUtcOffset(DateTime.Today.AddHours(9)));
        var yesterday = new DateTimeOffset(DateTime.Today.AddDays(-1).AddHours(20), local.GetUtcOffset(DateTime.Today.AddDays(-1)));
        var thisYear = new DateTimeOffset(new DateTime(DateTime.Today.Year, 3, 4, 15, 0, 0, DateTimeKind.Local));
        var lastYear = new DateTimeOffset(new DateTime(DateTime.Today.Year - 1, 12, 31, 8, 0, 0, DateTimeKind.Local));

        Assert.Equal("09:00", ConversationTime.Label(today.ToUnixTimeMilliseconds(), now));
        Assert.Equal(ConversationTime.YesterdayLabel, ConversationTime.Label(yesterday.ToUnixTimeMilliseconds(), now));
        Assert.Equal("03-04", ConversationTime.Label(thisYear.ToUnixTimeMilliseconds(), now));
        Assert.Equal((DateTime.Today.Year - 1) + "-12-31", ConversationTime.Label(lastYear.ToUnixTimeMilliseconds(), now));
    }

    [Fact]
    public void AFirstMessageNamesTheConversationAndLongOnesAreCutOnAGrapheme()
    {
        Assert.Equal("帮我总结这段会议记录", ConversationTitle.FromFirstMessage("  帮我总结这段会议记录  "));
        Assert.Equal("第一行 第二行", ConversationTitle.FromFirstMessage("第一行\n\n第二行"));
        Assert.Null(ConversationTitle.FromFirstMessage("   "));

        var longText = new string('字', ConversationTitle.MaxTextElements + 6);
        var title = ConversationTitle.FromFirstMessage(longText);
        Assert.Equal(ConversationTitle.MaxTextElements + 1, title!.Length);
        Assert.EndsWith("…", title, StringComparison.Ordinal);
    }

    [Fact]
    public void Uuid7IdsSortByCreationTimeAndCarryTheVersionBits()
    {
        var earlier = Uuid7.NewId(new DateTimeOffset(2026, 11, 23, 1, 0, 0, TimeSpan.Zero));
        var later = Uuid7.NewId(new DateTimeOffset(2026, 11, 23, 1, 0, 1, TimeSpan.Zero));

        Assert.Equal(36, earlier.Length);
        Assert.Equal(36, later.Length);
        Assert.Equal('-', earlier[8]);
        Assert.True(string.CompareOrdinal(earlier, later) < 0);
        Assert.Equal('7', earlier[14]);
        Assert.Contains(earlier[19], "89ab");
        Assert.Equal(
            new DateTimeOffset(2026, 11, 23, 1, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            Uuid7.TimestampOf(earlier));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class DatabaseFixture : IDisposable
    {
        private readonly string _directory;

        public DatabaseFixture()
        {
            _directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "neochat-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            DatabasePath = System.IO.Path.Combine(_directory, "neochat.db");
        }

        public string DatabasePath { get; }

        public NeoChatDatabase Open() => NeoChatDatabase.Open(DatabasePath);

        public SqliteConversationRepository OpenRepository() => new(Open());

        public void Execute(string sql)
        {
            using var connection = OpenRaw();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public string? Scalar(string sql)
        {
            using var connection = OpenRaw();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            var value = command.ExecuteScalar();
            return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // A temp directory that cannot be removed must not fail the assertion that just passed.
            }
        }

        private SqliteConnection OpenRaw()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
            }.ToString());
            connection.Open();
            return connection;
        }
    }
}
