using System.Globalization;
using System.Text.Json;
using NeoChat.Protocol.Events;

namespace NeoChat.Protocol.Errors;

/// <summary>Per-stream context every error object is stamped with.</summary>
public sealed record ErrorContext
{
    public string? Provider { get; init; }

    public string? Model { get; init; }

    public string TraceId { get; init; } = ProtocolErrors.NewTraceId();
}

/// <summary>
/// Maps provider failures to the closed code set: spec/errors.md §5.1 for OpenAI Chat /
/// OpenAI-compatible / OpenRouter, plus the parse and transport codes of §3.
/// </summary>
public static class ErrorMapper
{
    private const int RetryAfterCapMs = 30000;

    public static ProviderError FromHttpStatus(int status, string? body, IReadOnlyDictionary<string, string>? headers, ErrorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var parsed = ParsedErrorBody.From(body);
        var code = MapStatus(status, parsed);
        var retryAfterMs = ReadRetryAfterMs(headers);
        return ProtocolErrors.Create(
            code,
            context.TraceId,
            context.Provider,
            context.Model,
            httpStatus: status,
            providerCode: parsed.Code,
            retryable: ShouldRetryHeader(headers) ? true : null,
            retryAfterMs: retryAfterMs,
            parameters: ParamMapFor(code, parsed),
            raw: Security.Redactor.Redact(body),
            requestId: ReadHeader(headers, "x-request-id") ?? ReadHeader(headers, "request-id"));
    }

    /// <summary>Maps an error object that arrives inside an already accepted (2xx) stream.</summary>
    public static ProviderError FromProviderErrorBody(string body, JsonElement error, int? httpStatus, ErrorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var parsed = ParsedErrorBody.FromElement(error);
        // A 2xx status says nothing about the failure, so only an error status may drive the mapping.
        var numericStatus = NumericStatus(parsed.Code)
            ?? (httpStatus is { } errorStatus && errorStatus >= 400 ? errorStatus : null);
        var code = numericStatus is { } status
            ? MapStatus(status, parsed)
            : MapErrorCode(parsed);

        return ProtocolErrors.Create(
            code,
            context.TraceId,
            context.Provider,
            context.Model,
            httpStatus: numericStatus,
            providerCode: parsed.Code,
            parameters: ParamMapFor(code, parsed),
            raw: Security.Redactor.Redact(body));
    }

    public static ProviderError ParseInvalid(ErrorContext context, string? raw, string? providerCode = null) =>
        ProtocolErrors.Create(
            ErrorCatalog.ParseSseInvalid,
            context.TraceId,
            context.Provider,
            context.Model,
            providerCode: providerCode,
            parameters: new Dictionary<string, string>(StringComparer.Ordinal) { ["reason"] = "body_not_sse" },
            raw: Security.Redactor.Redact(raw));

    public static ProviderError EventTooLarge(ErrorContext context) =>
        ProtocolErrors.Create(
            ErrorCatalog.ParseEventTooLarge,
            context.TraceId,
            context.Provider,
            context.Model,
            parameters: new Dictionary<string, string>(StringComparer.Ordinal) { ["limit_bytes"] = "16777216" });

    public static ProviderError Incomplete(ErrorContext context, int partialTextLength) =>
        ProtocolErrors.Create(
            ErrorCatalog.ParseIncomplete,
            context.TraceId,
            context.Provider,
            context.Model,
            parameters: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["partial_text_length"] = partialTextLength.ToString(CultureInfo.InvariantCulture),
            });

    public static ProviderError ConnectionReset(ErrorContext context, string? detail) =>
        ProtocolErrors.Create(
            ErrorCatalog.NetConnectionReset,
            context.TraceId,
            context.Provider,
            context.Model,
            raw: Security.Redactor.Redact(detail));

    public static ProviderError ReadTimeout(ErrorContext context, long idleMs) =>
        ProtocolErrors.Create(
            ErrorCatalog.NetReadTimeout,
            context.TraceId,
            context.Provider,
            context.Model,
            parameters: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["idle_ms"] = idleMs.ToString(CultureInfo.InvariantCulture),
            });

    public static ProviderError Internal(ErrorContext context, string detail) =>
        ProtocolErrors.Create(ErrorCatalog.Internal, context.TraceId, context.Provider, context.Model, raw: Security.Redactor.Redact(detail));

    private static string MapStatus(int status, ParsedErrorBody parsed) => status switch
    {
        401 => ErrorCatalog.AuthInvalidKey,
        403 => MentionsRegion(parsed) ? ErrorCatalog.ContentFiltered : ErrorCatalog.AuthInvalidKey,
        404 => MentionsModel(parsed) ? ErrorCatalog.ModelNotFound : ErrorCatalog.EndpointNotFound,
        429 => IsInsufficientQuota(parsed) ? ErrorCatalog.AuthInsufficientQuota : ErrorCatalog.RateLimited,
        400 => MapBadRequest(parsed),
        402 => ErrorCatalog.AuthInsufficientQuota,
        413 => ErrorCatalog.ContextOverflow,
        500 or 502 or 503 or 504 => ErrorCatalog.ServerError,
        529 => ErrorCatalog.ServerOverloaded,
        _ => ErrorCatalog.UnknownProviderError,
    };

    private static string MapBadRequest(ParsedErrorBody parsed)
    {
        if (IsContextOverflow(parsed))
        {
            return ErrorCatalog.ContextOverflow;
        }

        return MentionsContentFilter(parsed) ? ErrorCatalog.ContentFiltered : ErrorCatalog.ParamInvalid;
    }

    private static string MapErrorCode(ParsedErrorBody parsed)
    {
        var code = parsed.Code?.Trim().ToLowerInvariant();
        return code switch
        {
            "invalid_api_key" or "authentication_error" or "invalid_token" => ErrorCatalog.AuthInvalidKey,
            "insufficient_quota" or "insufficient_quota_error" or "billing_hard_limit_reached" => ErrorCatalog.AuthInsufficientQuota,
            "rate_limit_exceeded" or "rate_limit_error" or "too_many_requests" => ErrorCatalog.RateLimited,
            "context_length_exceeded" or "request_too_large" => ErrorCatalog.ContextOverflow,
            "content_filter" or "content_policy_violation" => ErrorCatalog.ContentFiltered,
            "model_not_found" => ErrorCatalog.ModelNotFound,
            "server_error" or "api_error" => ErrorCatalog.ServerError,
            "overloaded_error" => ErrorCatalog.ServerOverloaded,
            null when IsContextOverflow(parsed) => ErrorCatalog.ContextOverflow,
            null when IsInsufficientQuota(parsed) => ErrorCatalog.AuthInsufficientQuota,
            _ => ErrorCatalog.UnknownProviderError,
        };
    }

    private static bool IsInsufficientQuota(ParsedErrorBody parsed) =>
        string.Equals(parsed.Code, "insufficient_quota", StringComparison.OrdinalIgnoreCase)
        || Contains(parsed.Message, "insufficient_quota")
        || Contains(parsed.Message, "quota");

    private static bool IsContextOverflow(ParsedErrorBody parsed) =>
        string.Equals(parsed.Code, "context_length_exceeded", StringComparison.OrdinalIgnoreCase)
        || Contains(parsed.Message, "context_length_exceeded")
        || Contains(parsed.Message, "context length")
        || Contains(parsed.Message, "too many tokens")
        || Contains(parsed.Message, "prompt is too long");

    private static bool MentionsContentFilter(ParsedErrorBody parsed) =>
        Contains(parsed.Code, "content_filter")
        || Contains(parsed.Code, "content_policy")
        || Contains(parsed.Message, "content_filter")
        || Contains(parsed.Message, "content_policy")
        || Contains(parsed.Message, "content policy");

    private static bool MentionsRegion(ParsedErrorBody parsed) =>
        Contains(parsed.Message, "country") || Contains(parsed.Message, "region");

    private static bool MentionsModel(ParsedErrorBody parsed) =>
        Contains(parsed.Message, "model") || Contains(parsed.Code, "model");

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static SortedDictionary<string, string>? ParamMapFor(string code, ParsedErrorBody parsed)
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (code == ErrorCatalog.ParamInvalid && parsed.Param is { Length: > 0 } param)
        {
            map["field"] = param;
        }

        if (code == ErrorCatalog.AuthInvalidKey)
        {
            var hint = Security.Redactor.Redact(parsed.Message ?? string.Empty);
            if (hint.Length > 0)
            {
                map["hint"] = hint;
            }
        }

        return map.Count == 0 ? null : map;
    }

    private static int? NumericStatus(string? code) =>
        int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value is >= 100 and <= 599
            ? value
            : null;

    private static int? ReadRetryAfterMs(IReadOnlyDictionary<string, string>? headers)
    {
        var raw = ReadHeader(headers, "retry-after");
        if (raw is null)
        {
            return null;
        }

        int milliseconds;
        if (int.TryParse(raw.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            milliseconds = seconds * 1000;
        }
        else if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when))
        {
            milliseconds = (int)Math.Max(0, (when - DateTimeOffset.UtcNow).TotalMilliseconds);
        }
        else
        {
            return null;
        }

        return Math.Clamp(milliseconds, 0, RetryAfterCapMs);
    }

    private static bool ShouldRetryHeader(IReadOnlyDictionary<string, string>? headers) =>
        string.Equals(ReadHeader(headers, "x-should-retry"), "true", StringComparison.OrdinalIgnoreCase);

    private static string? ReadHeader(IReadOnlyDictionary<string, string>? headers, string name)
    {
        if (headers is null)
        {
            return null;
        }

        foreach (var (key, value) in headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Tolerant reader for provider error bodies: OpenAI, OpenRouter and plain-text proxies.</summary>
    private sealed record ParsedErrorBody(string? Code, string? Type, string? Message, string? Param)
    {
        public static ParsedErrorBody From(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return new ParsedErrorBody(null, null, null, null);
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("error", out var error)
                    && error.ValueKind == JsonValueKind.Object)
                {
                    return FromElement(error);
                }

                return new ParsedErrorBody(null, null, body, null);
            }
            catch (JsonException)
            {
                return new ParsedErrorBody(null, null, body, null);
            }
        }

        public static ParsedErrorBody FromElement(JsonElement error) => new(
            ReadScalar(error, "code"),
            ReadScalar(error, "type"),
            ReadScalar(error, "message"),
            ReadScalar(error, "param"));
    }

    private static string? ReadScalar(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }
}
