using System.Globalization;
using Microsoft.Data.Sqlite;
using NeoChat.Windows.Core.Chat;
using NeoChat.Windows.Core.Storage;
using Xunit;

namespace NeoChat.Windows.Core.Tests;

/// <summary>
/// 消息操作的三件事：复制取到的内容、重新生成开出的兄弟分支、删除是软删。
/// 断言分两层——视图看到的那份列表，以及库里真正落下的行（deleted_at / sibling_index / active_leaf）。
/// </summary>
public sealed class MessageActionTests
{
    private static ProviderSettings Configured => new()
    {
        BaseUrl = "https://example.invalid/v1",
        ApiKey = "test-key-not-a-secret",
        Model = "test-model",
    };

    [Fact]
    public async Task CopyTakesTheReplySourceAndEachCodeBlockSeparately()
    {
        const string Markdown = """
            先看这段：

            ```csharp
            var answer = 42;
            ```

            列表里也有：

            - 嵌套的那个：

            ```python
            print("hi")
            ```
            """;

        var blocks = MessageContent.CodeBlocks(Markdown);

        Assert.Equal(2, blocks.Count);
        Assert.Equal("var answer = 42;", blocks[0].TrimEnd());
        Assert.Equal("print(\"hi\")", blocks[1].TrimEnd());

        // 复制全文交出去的是这条回复的 Markdown 原文（UI 直接拷 item.Text），不被渲染层改写。
        var client = new ScriptedChatStreamClient().Then(Events.Delta(Markdown, 1), Events.Done(2));
        using var viewModel = new ChatSessionViewModel(client, new InMemoryFailureLog(), Configured);
        viewModel.ComposerText = "给个例子";
        await viewModel.SendAsync();

        Assert.Equal(Markdown, viewModel.Messages[1].Text);
        Assert.Equal(blocks, MessageContent.CodeBlocks(viewModel.Messages[1].Text));
    }

    [Fact]
    public async Task Regenerate_OpensASiblingBranchUnderTheSameParent()
    {
        using var fixture = new DatabaseFixture();
        using var repository = fixture.OpenRepository();
        var client = new ScriptedChatStreamClient()
            .Then(Events.Delta("第一答", 1), Events.Done(2))
            .Then(Events.Delta("第二答", 3), Events.Done(4));
        using var viewModel = new ChatSessionViewModel(client, new InMemoryFailureLog(), Configured, repository);
        viewModel.OpenConversation(repository.EnsureOpenConversation().Id);

        viewModel.ComposerText = "问一句";
        await viewModel.SendAsync();

        var firstReply = viewModel.Messages[1];
        var firstReplyId = firstReply.RowId!;
        var userRowId = viewModel.Messages[0].RowId!;

        await viewModel.RegenerateAsync(firstReply);

        var secondReplyId = viewModel.Messages[1].RowId!;
        Assert.NotEqual(firstReplyId, secondReplyId);
        Assert.Equal("第二答", viewModel.Messages[1].Text);
        Assert.Equal(2, viewModel.Messages.Count);

        // 两个回答同父：旧的那条留在库里，新的成为兄弟，active_leaf 指向新的。
        Assert.Equal(userRowId, fixture.Scalar("SELECT parent_id FROM message WHERE id = '" + secondReplyId + "'"));
        Assert.Equal(userRowId, fixture.Scalar("SELECT parent_id FROM message WHERE id = '" + firstReplyId + "'"));
        Assert.Equal("0", fixture.Scalar("SELECT sibling_index FROM message WHERE id = '" + firstReplyId + "'"));
        Assert.Equal("1", fixture.Scalar("SELECT sibling_index FROM message WHERE id = '" + secondReplyId + "'"));
        Assert.Equal(
            "2",
            fixture.Scalar(
                "SELECT COUNT(*) FROM message WHERE parent_id = '" + userRowId + "' AND role = 'assistant' AND deleted_at IS NULL"));
        Assert.Equal(
            secondReplyId,
            fixture.Scalar("SELECT active_leaf_message_id FROM conversation WHERE id = '" + viewModel.ConversationId + "'"));
    }

    [Fact]
    public async Task Delete_IsASoftDeleteThatAlsoLeavesTheThreadAndTheSearchIndex()
    {
        using var fixture = new DatabaseFixture();
        using var repository = fixture.OpenRepository();
        var client = new ScriptedChatStreamClient().Then(Events.Delta("待删的回答", 1), Events.Done(2));
        using var viewModel = new ChatSessionViewModel(client, new InMemoryFailureLog(), Configured, repository);
        viewModel.OpenConversation(repository.EnsureOpenConversation().Id);

        viewModel.ComposerText = "问一句";
        await viewModel.SendAsync();

        var replyId = viewModel.Messages[1].RowId!;
        var userRowId = viewModel.Messages[0].RowId!;
        Assert.Equal("1", fixture.Scalar("SELECT COUNT(*) FROM message_fts WHERE message_id = '" + replyId + "'"));

        viewModel.DeleteMessage(viewModel.Messages[1]);

        Assert.Single(viewModel.Messages);
        Assert.Equal("1", fixture.Scalar("SELECT COUNT(*) FROM message WHERE id = '" + replyId + "'"));
        Assert.Equal(
            "1",
            fixture.Scalar("SELECT COUNT(*) FROM message WHERE id = '" + replyId + "' AND deleted_at IS NOT NULL"));
        Assert.Equal("0", fixture.Scalar("SELECT COUNT(*) FROM message_fts WHERE message_id = '" + replyId + "'"));
        Assert.Equal(
            userRowId,
            fixture.Scalar("SELECT active_leaf_message_id FROM conversation WHERE id = '" + viewModel.ConversationId + "'"));
        Assert.Equal(
            "1",
            fixture.Scalar("SELECT message_count FROM conversation WHERE id = '" + viewModel.ConversationId + "'"));

        // 重新打开会话：被测掉的那条不该再出现在线程里。
        viewModel.OpenConversation(viewModel.ConversationId!);
        Assert.Single(viewModel.Messages);
        Assert.Equal("问一句", viewModel.Messages[0].Text);
    }

    [Fact]
    public async Task ActionsAreRefusedWhileStreaming()
    {
        var client = new ScriptedChatStreamClient().ThenWaitForCancel(out var delivered, Events.Delta("一名", 1));
        using var viewModel = new ChatSessionViewModel(client, new InMemoryFailureLog(), Configured);

        viewModel.ComposerText = "第一问";
        var sending = viewModel.SendAsync();
        await delivered();

        var body = viewModel.Messages[1];
        await viewModel.RegenerateAsync(body);
        viewModel.DeleteMessage(body);

        Assert.Equal(2, viewModel.Messages.Count);
        viewModel.StopCommand.Execute(null);
        await sending;
        Assert.Equal(1, client.Attempts);
    }

    private sealed class DatabaseFixture : IDisposable
    {
        private readonly string _directory;

        public DatabaseFixture()
        {
            _directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "neochat-action-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            DatabasePath = System.IO.Path.Combine(_directory, "neochat.db");
        }

        public string DatabasePath { get; }

        public SqliteConversationRepository OpenRepository() => new(NeoChatDatabase.Open(DatabasePath));

        public string? Scalar(string sql)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
            }.ToString());
            connection.Open();
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
                // 临时目录删不掉，不该让刚通过的断言变成失败。
            }
        }
    }
}
