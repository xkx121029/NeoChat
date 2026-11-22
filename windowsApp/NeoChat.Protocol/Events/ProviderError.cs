namespace NeoChat.Protocol.Events;

/// <summary>
/// One clickable follow-up action. Parameter keys are the actions closed set in spec/errors.md §4;
/// only the keys that action carries are serialized.
/// </summary>
public sealed record ErrorAction
{
    private ErrorAction(string code)
    {
        A = code;
    }

    public string A { get; }

    public string? Focus { get; init; }

    public string? Topic { get; init; }

    public int? Ms { get; init; }

    public long? Index { get; init; }

    public IReadOnlyList<string>? Require { get; init; }

    public string? ModelId { get; init; }

    public static ErrorAction Simple(string code) => new(code);

    public static ErrorAction RetryAfter(int ms) => new("retry_after") { Ms = ms };

    public static ErrorAction EditProvider(string focus) => new("edit_provider") { Focus = focus };

    public static ErrorAction OpenDocs(string topic) => new("open_docs") { Topic = topic };

    public static ErrorAction SwitchModel(params string[] require) => new("switch_model") { Require = require.Length == 0 ? null : require };

    public static ErrorAction ViewCapabilityMatrix(string modelId) => new("view_capability_matrix") { ModelId = modelId };

    public static ErrorAction RetryTool(long index) => new("retry_tool") { Index = index };
}

/// <summary>The complete error object (spec/errors.md §2), carried by <c>failed.error</c>.</summary>
public sealed record ProviderError
{
    public required string Code { get; init; }

    public required string Stage { get; init; }

    public string? Provider { get; init; }

    public string? Model { get; init; }

    public int? HttpStatus { get; init; }

    public string? ProviderCode { get; init; }

    public required bool Retryable { get; init; }

    public int? RetryAfterMs { get; init; }

    public required string TitleKey { get; init; }

    public IReadOnlyDictionary<string, string> Params { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public required IReadOnlyList<string> Causes { get; init; }

    public required IReadOnlyList<ErrorAction> Actions { get; init; }

    public string? Raw { get; init; }

    public string? RequestId { get; init; }

    public required string TraceId { get; init; }
}
