using NeoChat.Protocol.Errors;

namespace NeoChat.Protocol.Tests;

/// <summary>ERR-001..ERR-006: error mapping and the transport failures of spec/errors.md §5.1.</summary>
public sealed class ErrorMappingTests
{
    private static Task<StreamRun> RunStatusAsync(int status, string body, IReadOnlyDictionary<string, string>? headers = null) =>
        Harness.RunAsync(Harness.Utf8(body), contentType: "application/json", statusCode: status, headers: headers);

    [Fact] // ERR-001
    public async Task Err001_Http401_InvalidApiKey()
    {
        var run = await RunStatusAsync(401, "{\"error\":{\"message\":\"Incorrect API key provided: sk-abcdefghijklmnopqrstuvwx\",\"type\":\"invalid_request_error\",\"code\":\"invalid_api_key\"}}");

        var error = run.AssertFailed(ErrorCatalog.AuthInvalidKey);
        Assert.False(error.Retryable);
        Assert.Equal("provider", error.Stage);
        Assert.Equal(401, error.HttpStatus);
        Assert.Contains(error.Actions, a => a.A == "edit_provider" && a.Focus == "key");
        Assert.Contains("sk-a…uvwx", error.Raw!, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwx", error.Raw!, StringComparison.Ordinal);
    }

    [Fact] // ERR-002
    public async Task Err002_Http429_InsufficientQuota()
    {
        var run = await RunStatusAsync(429, "{\"error\":{\"message\":\"You exceeded your current quota\",\"type\":\"insufficient_quota\",\"code\":\"insufficient_quota\"}}");

        var error = run.AssertFailed(ErrorCatalog.AuthInsufficientQuota);
        Assert.False(error.Retryable);
        Assert.Contains(error.Actions, a => a.A == "top_up");
    }

    [Fact]
    public async Task RetryAfterHeader_BecomesRetryAfterMsCappedAt30s()
    {
        var headers = new Dictionary<string, string> { ["Retry-After"] = "120" };
        var run = await RunStatusAsync(429, "{\"error\":{\"message\":\"slow down\",\"code\":\"rate_limit_exceeded\"}}", headers);

        var error = run.AssertFailed(ErrorCatalog.RateLimited);
        Assert.True(error.Retryable);
        Assert.Equal(30000, error.RetryAfterMs);
        Assert.Contains(error.Actions, a => a.A == "retry_after" && a.Ms == 30000);
    }

    [Fact] // ERR-003
    public async Task Err003_Http400_ContextLengthExceeded()
    {
        var run = await RunStatusAsync(400, "{\"error\":{\"message\":\"This model's maximum context length is 8192 tokens\",\"type\":\"invalid_request_error\",\"code\":\"context_length_exceeded\",\"param\":\"messages\"}}");

        var error = run.AssertFailed(ErrorCatalog.ContextOverflow);
        Assert.Contains(error.Actions, a => a.A == "reduce_context");
    }

    [Fact] // ERR-004
    public async Task Err004_Http404_ModelNotFound()
    {
        var run = await RunStatusAsync(404, "{\"error\":{\"message\":\"The model `gpt-9` does not exist or you do not have access to it\",\"code\":\"model_not_found\"}}");

        var error = run.AssertFailed(ErrorCatalog.ModelNotFound);
        Assert.Contains(error.Actions, a => a.A == "refresh_models");

        var endpoint = await RunStatusAsync(404, "{\"error\":{\"message\":\"not here\"}}");
        endpoint.AssertFailed(ErrorCatalog.EndpointNotFound);
    }

    [Fact] // ERR-005
    public async Task Err005_TcpResetMidStream_FailsWithConnectionResetAndKeepsContent()
    {
        var counter = new ActiveStreamCounter();
        await using var server = RawHttpServer.Start(async (srv, stream, socket, _) =>
        {
            await srv.WriteHeadAsync(stream, CancellationToken.None);
            await srv.WriteAsync(stream, Harness.ContentFrame("已生成的部分"), CancellationToken.None);
            await Task.Delay(300, CancellationToken.None);
            socket.LingerState = new System.Net.Sockets.LingerOption(true, 0);
            socket.Close();
        });

        var (client, chat, _) = await RawHttpServer.ConnectAsync(server.Url, Harness.Options(), counter);
        using (client)
        {
            var events = new List<StreamEvent>();
            await foreach (var streamEvent in chat)
            {
                events.Add(streamEvent);
            }

            var run = StreamRun.From(events, chat.Trace, false);
            var error = run.AssertFailed(ErrorCatalog.NetConnectionReset);
            Assert.Equal("stream_read", error.Stage);
            Assert.True(error.Retryable);
            Assert.Equal("已生成的部分", run.Text);
            Assert.Equal(0, counter.Count);
        }
    }

    [Fact] // ERR-006
    public async Task Err006_SilentStreamAfterFirstBytes_FailsWithReadTimeout()
    {
        var counter = new ActiveStreamCounter();
        await using var server = RawHttpServer.Start(async (srv, stream, _, _) =>
        {
            await srv.WriteHeadAsync(stream, CancellationToken.None);
            await srv.WriteAsync(stream, Harness.ContentFrame("开始"), CancellationToken.None);
            await Task.Delay(8000, CancellationToken.None);
        });

        var options = Harness.Options(readIdle: TimeSpan.FromMilliseconds(500));
        var (client, chat, _) = await RawHttpServer.ConnectAsync(server.Url, options, counter);
        using (client)
        {
            var events = new List<StreamEvent>();
            await foreach (var streamEvent in chat)
            {
                events.Add(streamEvent);
            }

            var run = StreamRun.From(events, chat.Trace, false);
            var error = run.AssertFailed(ErrorCatalog.NetReadTimeout);
            Assert.Equal("500", error.Params["idle_ms"]);
            Assert.Equal("开始", run.Text);
        }
    }

    [Fact]
    public async Task UnknownProviderError_KeepsRawAndCopyDiagnostics()
    {
        var run = await RunStatusAsync(418, "{\"weird\":\"gateway\"}");
        var error = run.AssertFailed(ErrorCatalog.UnknownProviderError);

        Assert.NotNull(error.Raw);
        Assert.Contains(error.Actions, a => a.A == "copy_diagnostics");
        Assert.Contains(error.Actions, a => a.A == "view_raw");
        Assert.Equal(3, error.Causes.Count);
    }

    [Fact]
    public async Task ShouldRetryHeaderOverridesRetryable()
    {
        var headers = new Dictionary<string, string> { ["x-should-retry"] = "true" };
        var run = await RunStatusAsync(500, "{\"error\":{\"message\":\"boom\"}}", headers);
        var error = run.AssertFailed(ErrorCatalog.ServerError);

        Assert.True(error.Retryable);
        Assert.Equal("500", error.Params["http_status"]);
    }
}
