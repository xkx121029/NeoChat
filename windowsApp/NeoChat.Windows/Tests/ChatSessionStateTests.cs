using Xunit;
using NeoChat.Protocol.Canonical;
using NeoChat.Protocol.Errors;
using NeoChat.Protocol.Events;
using NeoChat.Windows.Core.Chat;

namespace NeoChat.Windows.Core.Tests;

public sealed class ChatSessionStateTests
{
    private static ProviderSettings Configured => new()
    {
        BaseUrl = "https://example.invalid/v1",
        ApiKey = "test-key-not-a-secret",
        Model = "test-model",
    };

    [Fact]
    public async Task StreamedReply_ProducesExpectedStateSequence()
    {
        var client = new ScriptedChatStreamClient()
            .Then(
                Events.Reasoning("先想一下", 1),
                Events.Delta("你", 2),
                Events.Delta("好", 3),
                Events.Usage(4, 10, 2),
                Events.Done(5));
        var failureLog = new InMemoryFailureLog();
        using var viewModel = new ChatSessionViewModel(client, failureLog, Configured);
        var recorder = StateRecorder.Attach(viewModel);

        viewModel.ComposerText = "在吗";
        await viewModel.SendAsync();

        Assert.Equal("Idle→Connecting→Streaming→Idle", string.Join("→", recorder.Session));
        Assert.Equal("Sending→Complete", recorder.ForIndex(viewModel, 0));
        Assert.Equal("Sending→Streaming→Complete", recorder.ForIndex(viewModel, 1));
        Assert.Equal("你好", viewModel.Messages[1].Text);
        Assert.Equal("先想一下", viewModel.Messages[1].ReasoningText);
        Assert.Equal(10, viewModel.Messages[1].Usage!.Input);
        Assert.Empty(failureLog.Entries);
        Assert.Empty(viewModel.ComposerText);
        Assert.Equal(0, viewModel.CharacterCount);

        var request = Assert.IsType<ChatRequest>(client.LastRequest);
        Assert.Equal("test-model", request.Model);
        Assert.True(request.Stream);
        var only = Assert.Single(request.Messages);
        Assert.Equal("user", only.Role);
        Assert.Equal("在吗", Assert.IsType<TextPart>(Assert.Single(only.Parts)).Text);
    }

    [Fact]
    public async Task EveryDeltaIsAppliedToTheAssistantMessageAsItArrives()
    {
        var client = new ScriptedChatStreamClient()
            .Then(Events.Delta("一", 1), Events.Delta("二三", 2), Events.Delta("四五六", 3), Events.Done(4));
        using var viewModel = new ChatSessionViewModel(client, new InMemoryFailureLog(), Configured);
        var lengths = new List<int>();
        viewModel.Messages.CollectionChanged += (_, e) =>
        {
            foreach (var item in e.NewItems?.Cast<ChatMessageItem>() ?? [])
            {
                item.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(ChatMessageItem.Text) && item.IsAssistant)
                    {
                        lengths.Add(item.Text.Length);
                    }
                };
            }
        };

        viewModel.ComposerText = "问";
        await viewModel.SendAsync();

        Assert.Equal([1, 3, 6], lengths);
        Assert.Equal("一二三四五六", viewModel.Messages[1].Text);
    }

    [Fact]
    public async Task SecondTurn_SendsBothSidesOfTheConversationAsHistory()
    {
        var client = new ScriptedChatStreamClient()
            .Then(Events.Delta("第一答", 1), Events.Done(2))
            .Then(Events.Delta("第二答", 3), Events.Done(4));
        using var viewModel = new ChatSessionViewModel(client, new InMemoryFailureLog(), Configured);

        viewModel.ComposerText = "第一问";
        await viewModel.SendAsync();
        viewModel.ComposerText = "第二问";
        await viewModel.SendAsync();

        Assert.Equal(2, client.Attempts);
        var request = client.LastRequest!;
        Assert.Equal(["user", "assistant", "user"], request.Messages.Select(m => m.Role));
        Assert.Equal("第一答", Assert.IsType<TextPart>(request.Messages[1].Parts[0]).Text);
        Assert.Equal("第二问", Assert.IsType<TextPart>(request.Messages[2].Parts[0]).Text);
        Assert.Equal(4, viewModel.Messages.Count);
    }

    [Fact]
    public async Task SendIsIgnoredWhileStreaming()
    {
        var client = new ScriptedChatStreamClient().ThenWaitForCancel(out var delivered, Events.Delta("一名", 1));
        using var viewModel = new ChatSessionViewModel(client, new InMemoryFailureLog(), Configured);

        viewModel.ComposerText = "第一问";
        var sending = viewModel.SendAsync();
        await delivered();

        viewModel.ComposerText = "第二问";
        Assert.False(viewModel.CanSend);
        await viewModel.SendAsync();

        viewModel.StopCommand.Execute(null);
        await sending;
        Assert.Equal(1, client.Attempts);
        Assert.Equal(2, viewModel.Messages.Count);
    }
}
