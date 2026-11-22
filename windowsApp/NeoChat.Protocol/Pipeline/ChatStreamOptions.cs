namespace NeoChat.Protocol.Pipeline;

/// <summary>Protocol family of one stream. Only the OpenAI Chat family is implemented in M1.</summary>
public enum ProtocolFamily
{
    OpenAiChat,
    OpenAiCompatible,
    OpenRouter,
}

public sealed record ChatStreamOptions
{
    public required ProtocolFamily Protocol { get; init; }

    /// <summary>Provider label stamped on error objects, e.g. <c>openai_compatible</c>.</summary>
    public string? Provider { get; init; }

    public string? Model { get; init; }

    /// <summary>
    /// The endpoint re-sends the accumulated text instead of increments (spec §4.1 "重复 chunk 的去重判据").
    /// Also enabled by a conformance sample carrying <c>meta.duplicate=true</c>.
    /// </summary>
    public bool CumulativeStream { get; init; }

    /// <summary>Conformance <c>meta.duplicate=true</c>: the sample repeats identical chunks on purpose.</summary>
    public bool DuplicateFrames { get; init; }

    /// <summary>Payload ceiling per frame; spec §2.1 rule 10 fixes 16 MiB as the default and 1 MiB as the floor.</summary>
    public int MaxPayloadBytes { get; init; } = Transport.SseFramer.DefaultMaxPayloadBytes;

    /// <summary>read_idle: any transferred byte, heartbeats included, resets it (spec §2.2).</summary>
    public TimeSpan ReadIdleTimeout { get; init; } = TimeSpan.FromSeconds(180);

    public bool DeduplicatesText => CumulativeStream || DuplicateFrames;
}
