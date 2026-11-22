namespace NeoChat.Protocol.Diagnostics;

/// <summary>
/// Non-normative per-stream counters. They never reach the wire; conformance compares the event
/// stream, and these counters exist so tests can assert on the branch taken (spec §2.1 rule 9).
/// </summary>
public sealed class StreamTrace
{
    private readonly List<string> _notes = new();

    public long InvalidUtf8Bytes { get; set; }

    public long HeartbeatLines { get; set; }

    public long ReasoningFieldConflicts { get; set; }

    public long DuplicateFramesSkipped { get; set; }

    public long RewritesIgnored { get; set; }

    public long MultiChoiceDropped { get; set; }

    public long ToolCallsWithoutIndex { get; set; }

    public long ToolCallNamesMissing { get; set; }

    public long EventsDroppedAfterTerminal { get; set; }

    public IReadOnlyList<string> Notes => _notes;

    public void Note(string message) => _notes.Add(message);
}
