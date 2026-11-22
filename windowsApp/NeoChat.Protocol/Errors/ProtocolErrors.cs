using NeoChat.Protocol.Events;

namespace NeoChat.Protocol.Errors;

/// <summary>Builds complete error objects (spec/errors.md §2) from the closed code set.</summary>
public static class ProtocolErrors
{
    public static ProviderError Create(
        string code,
        string traceId,
        string? provider = null,
        string? model = null,
        int? httpStatus = null,
        string? providerCode = null,
        bool? retryable = null,
        int? retryAfterMs = null,
        IReadOnlyDictionary<string, string>? parameters = null,
        string? raw = null,
        string? requestId = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentException.ThrowIfNullOrEmpty(traceId);

        var spec = ErrorCatalog.Lookup(code);
        var mergedParams = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (parameters is not null)
        {
            foreach (var (key, value) in parameters)
            {
                mergedParams[key] = value;
            }
        }

        if (code == ErrorCatalog.ServerError && httpStatus is { } status)
        {
            mergedParams["http_status"] = status.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return new ProviderError
        {
            Code = spec.Code,
            Stage = spec.Stage,
            Provider = provider,
            Model = model,
            HttpStatus = httpStatus,
            ProviderCode = providerCode,
            Retryable = retryable ?? spec.Retryable,
            RetryAfterMs = retryAfterMs,
            TitleKey = $"errors.{spec.Code}.title",
            Params = mergedParams,
            Causes = spec.Causes,
            Actions = ResolveActions(spec, retryAfterMs),
            Raw = raw is null ? null : Security.Redactor.Redact(raw),
            RequestId = requestId,
            TraceId = traceId,
        };
    }

    /// <summary>UUIDv7, the local correlation id required by errors.md §2.</summary>
    public static string NewTraceId() => Guid.CreateVersion7().ToString("D");

    private static List<ErrorAction> ResolveActions(ErrorSpec spec, int? retryAfterMs)
    {
        var actions = new List<ErrorAction>(spec.Actions.Length);
        foreach (var action in spec.Actions)
        {
            if (action.A != "retry_after")
            {
                actions.Add(action);
                continue;
            }

            if (retryAfterMs is { } ms)
            {
                actions.Add(ErrorAction.RetryAfter(ms));
            }
            else
            {
                actions.Add(ErrorAction.Simple("retry"));
            }
        }

        return actions;
    }
}
