using Xunit;
using NeoChat.Protocol.Errors;
using NeoChat.Protocol.Events;
using NeoChat.Windows.Core.Chat;

namespace NeoChat.Windows.Core.Tests;

public sealed class ChatCancelAndFailureTests
{
    private static ProviderSettings Configured => new()
    {
        BaseUrl = "https://example.invalid/v1",
        ApiKey = "test-key-not-a-secret",
        Model = "test-model",
    };

    [Fact]
    public async Task StopMidStream_KeepsPartialTextAndReportsStopped()
    {
        var client = new ScriptedChatStreamClient().ThenWaitForCancel(out var delivered, Events.Delta("半", 1), Events.Delta("句", 2));
        var failureLog = new InMemoryFailureLog();
        using var viewModel = new ChatSessionViewModel(client, failureLog, Configured);
        var recorder = StateRecorder.Attach(viewModel);

        viewModel.ComposerText = "写一段";
        var sending = viewModel.SendAsync();
        await delivered();
        viewModel.StopCommand.Execute(null);
        await sending;

        var assistant = viewModel.Messages[1];
        Assert.Equal("半句", assistant.Text);
        Assert.Equal(MessageStatus.Stopped, assistant.Status);
        Assert.True(assistant.ShowInterruptedMarker);
        Assert.False(assistant.ShowStreamingCursor);
        Assert.Equal(SessionStatus.Stopped, viewModel.Status);
        Assert.Equal("已停止", viewModel.StatusText);
        Assert.Equal("Idle→Connecting→Streaming→Stopped", string.Join("→", recorder.Session));
        Assert.Equal("Sending→Streaming→Stopped", recorder.ForIndex(viewModel, 1));
        Assert.Empty(failureLog.Entries);
        Assert.Null(viewModel.Error);
    }

    [Fact]
    public async Task ProviderSideAbort_LandsInStoppedWithoutFailureRecord()
    {
        var client = new ScriptedChatStreamClient()
            .Then(Events.Delta("一", 1), Events.Done(2, FinishReason.AbortedUser));
        var failureLog = new InMemoryFailureLog();
        using var viewModel = new ChatSessionViewModel(client, failureLog, Configured);

        viewModel.ComposerText = "问";
        await viewModel.SendAsync();

        Assert.Equal(MessageStatus.Stopped, viewModel.Messages[1].Status);
        Assert.Equal(SessionStatus.Stopped, viewModel.Status);
        Assert.Empty(failureLog.Entries);
    }

    [Fact]
    public async Task FailedEvent_ShowsTitleCausesActionsAndPersistsOnce()
    {
        var error = ProtocolErrors.Create(
            ErrorCatalog.AuthInvalidKey,
            ProtocolErrors.NewTraceId(),
            provider: "openai_chat",
            model: "test-model",
            httpStatus: 401);
        var client = new ScriptedChatStreamClient().Then(Events.Delta("部分", 1), Events.Failed(2, error));
        var failureLog = new InMemoryFailureLog();
        using var viewModel = new ChatSessionViewModel(client, failureLog, Configured);
        var recorder = StateRecorder.Attach(viewModel);

        viewModel.ComposerText = "问";
        await viewModel.SendAsync();

        Assert.Equal("Sending→Streaming→Failed", recorder.ForIndex(viewModel, 1));
        Assert.Equal("Idle→Connecting→Streaming→Failed", string.Join("→", recorder.Session));
        Assert.Equal(MessageStatus.Failed, viewModel.Messages[1].Status);
        Assert.True(viewModel.Messages[1].HasError);
        Assert.Equal(SessionStatus.Failed, viewModel.Status);

        var banner = Assert.IsType<NeoChat.Windows.Core.Errors.ErrorPresentation>(viewModel.Error);
        Assert.Equal("密钥无效或已被撤销", banner.Title);
        Assert.Equal(
            ["密钥被撤销/重置", "密钥与端点不匹配（把 A 家的 Key 填进了 B 家）", "粘贴时被截断或含空格"],
            banner.Causes);
        Assert.Contains(banner.Actions, a => a.Code == "edit_provider" && a.Label == "编辑提供商");
        Assert.Contains(banner.Actions, a => a.Code == "check_base_url" && a.Label == "检查 Base URL");
        Assert.Contains(banner.Actions, a => a.Code == "copy_diagnostics" && a.Label == "复制诊断");

        var recorded = Assert.Single(failureLog.Entries);
        Assert.Equal(ErrorCatalog.AuthInvalidKey, recorded.Code);
        Assert.Equal(error.TraceId, recorded.TraceId);
    }

    [Fact]
    public async Task RetryAfterFailure_DropsFailedRowAndSucceeds()
    {
        var error = ProtocolErrors.Create(
            ErrorCatalog.NetReadTimeout,
            ProtocolErrors.NewTraceId(),
            provider: "openai_chat",
            model: "test-model");
        var client = new ScriptedChatStreamClient()
            .Then(Events.Delta("半截", 1), Events.Failed(2, error))
            .Then(Events.Delta("整段", 10), Events.Done(11));
        var failureLog = new InMemoryFailureLog();
        using var viewModel = new ChatSessionViewModel(client, failureLog, Configured);

        viewModel.ComposerText = "重试我";
        await viewModel.SendAsync();
        Assert.True(viewModel.CanRetry);

        await viewModel.RetryAsync();

        Assert.Equal(2, client.Attempts);
        Assert.Equal("整段", viewModel.Messages[1].Text);
        Assert.Equal(MessageStatus.Complete, viewModel.Messages[1].Status);
        Assert.Null(viewModel.Error);
        Assert.Equal(SessionStatus.Idle, viewModel.Status);
        Assert.Equal(2, viewModel.Messages.Count);
        Assert.Single(failureLog.Entries);
        Assert.Equal(["user"], client.LastRequest!.Messages.Select(m => m.Role));
    }

    [Fact]
    public async Task UnconfiguredProvider_FailsAtConfigStageWithoutNetwork()
    {
        var client = new ScriptedChatStreamClient();
        var failureLog = new InMemoryFailureLog();
        using var viewModel = new ChatSessionViewModel(client, failureLog, ProviderSettings.Unconfigured);

        viewModel.ComposerText = "问";
        await viewModel.SendAsync();

        Assert.Equal(0, client.Attempts);
        Assert.Equal(SessionStatus.Failed, viewModel.Status);
        Assert.Equal(MessageStatus.Failed, viewModel.Messages[1].Status);
        Assert.Equal("这个 Provider 还没有配置密钥", viewModel.Error!.Title);
        Assert.Contains(viewModel.Error.Actions, a => a.Code == "edit_provider");
        var recorded = Assert.Single(failureLog.Entries);
        Assert.Equal("AUTH_MISSING_KEY", recorded.Code);
        Assert.Equal("config", recorded.Stage);
    }

    [Fact]
    public async Task StreamWithoutTerminalEvent_IsReportedAsIncomplete()
    {
        var client = new ScriptedChatStreamClient().Then(Events.Delta("截", 1));
        var failureLog = new InMemoryFailureLog();
        using var viewModel = new ChatSessionViewModel(client, failureLog, Configured);

        viewModel.ComposerText = "问";
        await viewModel.SendAsync();

        Assert.Equal(MessageStatus.Failed, viewModel.Messages[1].Status);
        Assert.Equal("回复在中途被截断", viewModel.Error!.Title);
        Assert.Equal(ErrorCatalog.ParseIncomplete, Assert.Single(failureLog.Entries).Code);
    }

    [Fact]
    public async Task TransportException_MapsToConnectionReset()
    {
        var client = new ScriptedChatStreamClient().ThenThrow(new HttpRequestException("connection refused"));
        var failureLog = new InMemoryFailureLog();
        using var viewModel = new ChatSessionViewModel(client, failureLog, Configured);

        viewModel.ComposerText = "问";
        await viewModel.SendAsync();

        var recorded = Assert.Single(failureLog.Entries);
        Assert.Equal(ErrorCatalog.NetConnectionReset, recorded.Code);
        Assert.Equal("连接被中途断开", viewModel.Error!.Title);
        Assert.Equal(SessionStatus.Failed, viewModel.Status);
    }
}
