using Xunit;
using NeoChat.Windows.Core.Chat;

namespace NeoChat.Windows.Core.Tests;

public sealed class ComposerTests
{
    private static ProviderSettings Configured => new()
    {
        BaseUrl = "https://example.invalid/v1",
        ApiKey = "test-key-not-a-secret",
        Model = "test-model",
    };

    [Theory]
    [InlineData("", 0)]
    [InlineData("你好", 2)]
    [InlineData("你好🦊", 3)]
    [InlineData("e\u0301", 1)]
    public void CharacterCount_CountsGraphemes(string text, int expected)
    {
        using var viewModel = new ChatSessionViewModel(new ScriptedChatStreamClient(), new InMemoryFailureLog(), Configured);

        viewModel.ComposerText = text;

        Assert.Equal(expected, viewModel.CharacterCount);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("\n\t", false)]
    [InlineData("a", true)]
    public void CanSend_RequiresVisibleText(string text, bool expected)
    {
        using var viewModel = new ChatSessionViewModel(new ScriptedChatStreamClient(), new InMemoryFailureLog(), Configured);

        viewModel.ComposerText = text;

        Assert.Equal(expected, viewModel.CanSend);
    }

    [Fact]
    public void EmptyConversation_ShowsEmptyState()
    {
        using var viewModel = new ChatSessionViewModel(new ScriptedChatStreamClient(), new InMemoryFailureLog(), Configured);

        Assert.True(viewModel.ShowEmptyState);
        Assert.False(viewModel.HasMessages);
        Assert.Equal("就绪", viewModel.StatusText);
        Assert.Equal("未配置模型", new ChatSessionViewModel(new ScriptedChatStreamClient(), new InMemoryFailureLog(), ProviderSettings.Unconfigured).ModelLabel);
    }

    [Fact]
    public async Task StartNewConversation_ClearsRowsAndReturnsToIdle()
    {
        var client = new ScriptedChatStreamClient().Then(Events.Delta("答", 1), Events.Done(2));
        using var viewModel = new ChatSessionViewModel(client, new InMemoryFailureLog(), Configured);
        viewModel.ComposerText = "问";
        await viewModel.SendAsync();
        Assert.Equal(2, viewModel.Messages.Count);

        viewModel.StartNewConversation();

        Assert.Empty(viewModel.Messages);
        Assert.True(viewModel.ShowEmptyState);
        Assert.Equal(SessionStatus.Idle, viewModel.Status);
    }

    [Fact]
    public void ApplyProviderSettings_UpdatesModelLabel()
    {
        using var viewModel = new ChatSessionViewModel(new ScriptedChatStreamClient(), new InMemoryFailureLog(), ProviderSettings.Unconfigured);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.ApplyProviderSettings(Configured);

        Assert.Equal("test-model", viewModel.ModelLabel);
        Assert.Contains(nameof(ChatSessionViewModel.ModelLabel), changed);
    }
}
