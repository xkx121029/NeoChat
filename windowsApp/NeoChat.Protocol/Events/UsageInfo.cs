namespace NeoChat.Protocol.Events;

/// <summary>Provider-reported cost, only present when the provider states it (OpenRouter <c>cost</c>).</summary>
public sealed record ProviderCost(string Amount, string Currency);

/// <summary>Cumulative final token counts (spec/stream-events.md §3.2).</summary>
public sealed record UsageInfo
{
    public UsageInfo(long input, long output)
    {
        Input = input;
        Output = output;
    }

    public long Input { get; }

    public long Output { get; }

    public long? Cached { get; init; }

    public long? Reasoning { get; init; }

    public long? Total { get; init; }

    public bool? Estimated { get; init; }

    public double? ErrorBoundPct { get; init; }

    public ProviderCost? ProviderCost { get; init; }
}
