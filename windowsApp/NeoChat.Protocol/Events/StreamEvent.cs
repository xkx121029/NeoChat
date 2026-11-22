namespace NeoChat.Protocol.Events;

public enum StreamEventKind
{
    Delta,
    ReasoningDelta,
    ToolCallDelta,
    ToolCallEnd,
    Usage,
    Notice,
    Done,
    Failed,
}

/// <summary>One normalized stream event. Wire shape: spec/stream-events.md §3.2.</summary>
public sealed record StreamEvent
{
    public required StreamEventKind Kind { get; init; }

    public required int Seq { get; init; }

    public string? Text { get; init; }

    public int? Index { get; init; }

    public string? ToolCallId { get; init; }

    public string? ToolName { get; init; }

    public string? Args { get; init; }

    public UsageInfo? Usage { get; init; }

    public NoticeInfo? Notice { get; init; }

    public FinishReason? FinishReason { get; init; }

    public string? RawFinishReason { get; init; }

    public ProviderError? Error { get; init; }

    public bool IsTerminal => Kind is StreamEventKind.Done or StreamEventKind.Failed;

    public string ToJsonLine() => StreamEventJson.ToLine(this);
}
