using Xunit;
using NeoChat.Protocol.Errors;
using NeoChat.Protocol.Events;
using NeoChat.Windows.Core.Errors;

namespace NeoChat.Windows.Core.Tests;

public sealed class ErrorPresentationTests
{
    /// <summary>spec/errors.md §3 closed set, in table order.</summary>
    private static readonly string[] SpecCodes =
    [
        "NET_OFFLINE", "NET_DNS_FAILED", "NET_CONNECT_TIMEOUT", "NET_CONNECT_REFUSED", "NET_TLS_FAILED",
        "NET_PROXY_AUTH_FAILED", "NET_CONNECTION_RESET", "NET_READ_TIMEOUT",
        "AUTH_MISSING_KEY", "AUTH_INVALID_KEY", "AUTH_INSUFFICIENT_QUOTA", "RATE_LIMITED",
        "MODEL_NOT_FOUND", "PARAM_INVALID", "CONTEXT_OVERFLOW", "CAPABILITY_UNSUPPORTED", "CONTENT_FILTERED",
        "PARSE_SSE_INVALID", "PARSE_INCOMPLETE", "PARSE_EVENT_TOO_LARGE",
        "SERVER_ERROR", "SERVER_OVERLOADED", "ENDPOINT_NOT_FOUND", "UNKNOWN_PROVIDER_ERROR",
        "CONFIG_INVALID_BASE_URL", "KEY_STORE_UNAVAILABLE", "DB_WRITE_FAILED", "TOOL_EXEC_FAILED", "INTERNAL",
        "IMPORT_MALFORMED", "IMPORT_TOO_LARGE", "IMPORT_UNSUPPORTED_VERSION",
    ];

    [Fact]
    public void EverySpecCodeHasChineseTitleAndNoUnresolvedPlaceholder()
    {
        Assert.Equal(32, SpecCodes.Length);
        foreach (var code in SpecCodes)
        {
            Assert.True(ErrorCatalog.IsKnown(code), code + " is outside the protocol catalog");
            var presentation = ErrorPresentation.From(ProtocolErrors.Create(code, ProtocolErrors.NewTraceId(), httpStatus: 500));
            Assert.False(string.IsNullOrWhiteSpace(presentation.Title), code + " has no title");
            Assert.NotEqual(code, presentation.Title);
            Assert.DoesNotContain("{", presentation.Title, StringComparison.Ordinal);
            Assert.InRange(presentation.Causes.Count, 1, 3);
            Assert.All(presentation.Causes, cause => Assert.NotEqual("未在文案表中登记的原因", cause));
            Assert.NotEmpty(presentation.Actions);
            Assert.All(presentation.Actions, action => Assert.True(ErrorTextCatalog.IsKnownAction(action.Code), action.Code));
        }
    }

    [Fact]
    public void ServerErrorSubstitutesHttpStatus()
    {
        var presentation = ErrorPresentation.From(
            ProtocolErrors.Create(ErrorCatalog.ServerError, ProtocolErrors.NewTraceId(), httpStatus: 502));

        Assert.Equal("服务器内部错误（502）", presentation.Title);
    }

    [Fact]
    public void ActionsKeepClosedSetOrderAndLabels()
    {
        var presentation = ErrorPresentation.From(ProtocolErrors.Create(ErrorCatalog.RateLimited, ProtocolErrors.NewTraceId()));

        // ProtocolErrors turns retry_after into retry when the provider sent no Retry-After (spec §4).
        Assert.Equal(["重试", "编辑提供商", "管理密钥"], presentation.Actions.Select(a => a.Label));
        Assert.True(presentation.CanRetry);
    }

    [Fact]
    public void RetryActionCarriesRetryAfterMilliseconds()
    {
        var presentation = ErrorPresentation.From(
            ProtocolErrors.Create(ErrorCatalog.RateLimited, ProtocolErrors.NewTraceId(), retryAfterMs: 2500));

        var retry = Assert.Single(presentation.Actions, a => a.Code == "retry_after");
        Assert.Equal(2500, retry.RetryAfterMs);
    }

    [Fact]
    public void DiagnosticsTextCarriesCodeTraceAndRedactedRawOnly()
    {
        var presentation = ErrorPresentation.From(
            ProtocolErrors.Create(ErrorCatalog.ParamInvalid, ProtocolErrors.NewTraceId(), raw: "{\"error\":{\"message\":\"bad temp\"}}"));

        var text = presentation.ToDiagnosticsText();

        Assert.Contains("code=PARAM_INVALID", text, StringComparison.Ordinal);
        Assert.Contains(presentation.TraceId, text, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownNoticeStyleCodeFallsBackToItsCode()
    {
        Assert.Equal("SOMETHING_NEW", ErrorTextCatalog.Title("SOMETHING_NEW", new Dictionary<string, string>(StringComparer.Ordinal)));
    }
}
