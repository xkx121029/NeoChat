namespace NeoChat.Protocol.Events;

/// <summary>Closed set of terminal reasons (spec/stream-events.md §3.2).</summary>
public enum FinishReason
{
    Stop,
    Length,
    ToolCalls,
    ContentFilter,
    AbortedUser,
}

public static class FinishReasonWire
{
    public static string ToWire(FinishReason reason) => reason switch
    {
        FinishReason.Stop => "stop",
        FinishReason.Length => "length",
        FinishReason.ToolCalls => "tool_calls",
        FinishReason.ContentFilter => "content_filter",
        FinishReason.AbortedUser => "aborted_user",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "unknown finish reason"),
    };
}
