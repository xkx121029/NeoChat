namespace NeoChat.Protocol.Events;

/// <summary>Closed set of notice codes (spec/stream-events.md §3.2).</summary>
public enum NoticeCode
{
    CapabilityStripped,
    InputTruncated,
    MultiChoiceIgnored,
    UsageEstimated,
    ReasoningUnsupported,
    ProviderDowngraded,
    StreamInterrupted,
}

public static class NoticeCodeWire
{
    public static string ToWire(NoticeCode code) => code switch
    {
        NoticeCode.CapabilityStripped => "capability_stripped",
        NoticeCode.InputTruncated => "input_truncated",
        NoticeCode.MultiChoiceIgnored => "multi_choice_ignored",
        NoticeCode.UsageEstimated => "usage_estimated",
        NoticeCode.ReasoningUnsupported => "reasoning_unsupported",
        NoticeCode.ProviderDowngraded => "provider_downgraded",
        NoticeCode.StreamInterrupted => "stream_interrupted",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "unknown notice code"),
    };
}

/// <summary>Non-fatal hint. Array values stay arrays on the wire; they are never comma-joined.</summary>
public sealed record NoticeInfo
{
    public NoticeInfo(NoticeCode code, IReadOnlyDictionary<string, IReadOnlyList<string>>? parameters = null)
    {
        Code = code;
        Parameters = parameters;
    }

    public NoticeCode Code { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Parameters { get; }

    public static NoticeInfo Of(NoticeCode code, params (string Key, string Value)[] parameters)
    {
        if (parameters.Length == 0)
        {
            return new NoticeInfo(code);
        }

        var map = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (key, value) in parameters)
        {
            map[key] = new[] { value };
        }

        return new NoticeInfo(code, map);
    }
}
