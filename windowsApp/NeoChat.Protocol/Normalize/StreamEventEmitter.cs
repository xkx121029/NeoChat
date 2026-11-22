using NeoChat.Protocol.Diagnostics;
using NeoChat.Protocol.Events;

namespace NeoChat.Protocol.Normalize;

/// <summary>
/// The single place where I1..I12 are enforced on the way out. Adapters translate provider frames
/// into intent (delta / tool call / usage / notice / terminal); the emitter assigns <c>seq</c>,
/// suppresses empty text (I6), closes tool calls before the terminal event (I5), keeps
/// <c>usage</c> to one event before the terminal (I4), and refuses to emit after the terminal (I3).
/// </summary>
public sealed class StreamEventEmitter
{
    private readonly StreamTrace _trace;
    private readonly List<StreamEvent> _pending = new();
    private readonly List<int> _openToolIndexes = new();
    private readonly HashSet<int> _knownToolIndexes = new();
    private readonly HashSet<int> _closedToolIndexes = new();
    private readonly HashSet<NoticeCode> _emittedNotices = new();
    private UsageInfo? _pendingUsage;
    private int _nextSeq;
    private bool _terminated;

    public StreamEventEmitter(StreamTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        _trace = trace;
    }

    public bool IsTerminated => _terminated;

    public int NextSeq => _nextSeq;

    public IReadOnlyList<int> OpenToolIndexes => _openToolIndexes;

    public void Delta(string? text)
    {
        if (_terminated || string.IsNullOrEmpty(text))
        {
            return;
        }

        Add(new StreamEvent { Kind = StreamEventKind.Delta, Seq = _nextSeq++, Text = text });
    }

    public void ReasoningDelta(string? text)
    {
        if (_terminated || string.IsNullOrEmpty(text))
        {
            return;
        }

        Add(new StreamEvent { Kind = StreamEventKind.ReasoningDelta, Seq = _nextSeq++, Text = text });
    }

    /// <summary>
    /// I7: the first delta of an index carries <c>id</c> and <c>name</c> (id is synthesized as
    /// <c>call_&lt;index&gt;</c> when the provider omits it); later deltas carry <c>args</c> only.
    /// </summary>
    public void ToolCallDelta(int index, string? id, string? name, string? args)
    {
        if (_terminated || index < 0)
        {
            return;
        }

        if (_knownToolIndexes.Add(index))
        {
            _openToolIndexes.Add(index);
            if (string.IsNullOrEmpty(name))
            {
                _trace.ToolCallNamesMissing++;
                _trace.Note($"tool_call index {index} opened without a name");
            }

            Add(new StreamEvent
            {
                Kind = StreamEventKind.ToolCallDelta,
                Seq = _nextSeq++,
                Index = index,
                ToolCallId = string.IsNullOrEmpty(id) ? $"call_{index}" : id,
                ToolName = name ?? string.Empty,
                Args = args ?? string.Empty,
            });
            return;
        }

        if (string.IsNullOrEmpty(args))
        {
            return;
        }

        Add(new StreamEvent { Kind = StreamEventKind.ToolCallDelta, Seq = _nextSeq++, Index = index, Args = args });
    }

    public void ToolCallEnd(int index)
    {
        if (_terminated || index < 0 || !_closedToolIndexes.Add(index))
        {
            return;
        }

        if (!_knownToolIndexes.Contains(index))
        {
            _trace.Note($"tool_call_end for index {index} that was never opened");
        }

        _openToolIndexes.Remove(index);
        Add(new StreamEvent { Kind = StreamEventKind.ToolCallEnd, Seq = _nextSeq++, Index = index });
    }

    /// <summary>I4: at most one usage event, held until the terminal event so it always precedes it.</summary>
    public void Usage(UsageInfo usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        if (_terminated)
        {
            return;
        }

        _pendingUsage = _pendingUsage is null
            ? usage
            : new UsageInfo(usage.Input, usage.Output)
            {
                Cached = usage.Cached ?? _pendingUsage.Cached,
                Reasoning = usage.Reasoning ?? _pendingUsage.Reasoning,
                Total = usage.Total ?? _pendingUsage.Total,
                Estimated = usage.Estimated ?? _pendingUsage.Estimated,
                ErrorBoundPct = usage.ErrorBoundPct ?? _pendingUsage.ErrorBoundPct,
                ProviderCost = usage.ProviderCost ?? _pendingUsage.ProviderCost,
            };
    }

    /// <summary>One notice per code per stream, so a repeated condition cannot flood <c>seq</c>.</summary>
    public void Notice(NoticeInfo notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        if (_terminated || !_emittedNotices.Add(notice.Code))
        {
            return;
        }

        Add(new StreamEvent { Kind = StreamEventKind.Notice, Seq = _nextSeq++, Notice = notice });
    }

    public void Done(FinishReason reason, string? rawFinishReason = null)
    {
        if (_terminated)
        {
            return;
        }

        var effective = reason;
        if (effective == FinishReason.ToolCalls && _knownToolIndexes.Count == 0)
        {
            _trace.ToolCallsWithoutIndex++;
            _trace.Note("finish_reason tool_calls with no opened tool index; recorded as stop");
            effective = FinishReason.Stop;
        }

        foreach (var index in _openToolIndexes.Order())
        {
            ToolCallEnd(index);
        }

        FlushUsage();
        Add(new StreamEvent
        {
            Kind = StreamEventKind.Done,
            Seq = _nextSeq++,
            FinishReason = effective,
            RawFinishReason = rawFinishReason,
        });
        _terminated = true;
    }

    public void Failed(ProviderError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (_terminated)
        {
            return;
        }

        FlushUsage();
        Add(new StreamEvent { Kind = StreamEventKind.Failed, Seq = _nextSeq++, Error = error });
        _terminated = true;
    }

    /// <summary>I10: a user cancel always ends with <c>done{aborted_user}</c>, never <c>failed</c>.</summary>
    public void Canceled()
    {
        if (_terminated)
        {
            return;
        }

        foreach (var index in _openToolIndexes.Order())
        {
            ToolCallEnd(index);
        }

        FlushUsage();
        Add(new StreamEvent
        {
            Kind = StreamEventKind.Done,
            Seq = _nextSeq++,
            FinishReason = FinishReason.AbortedUser,
        });
        _terminated = true;
    }

    /// <summary>Takes the events produced since the last drain, in seq order.</summary>
    public IReadOnlyList<StreamEvent> Drain()
    {
        if (_pending.Count == 0)
        {
            return Array.Empty<StreamEvent>();
        }

        var drained = _pending.ToArray();
        _pending.Clear();
        return drained;
    }

    private void FlushUsage()
    {
        if (_pendingUsage is not { } usage)
        {
            return;
        }

        _pendingUsage = null;
        Add(new StreamEvent { Kind = StreamEventKind.Usage, Seq = _nextSeq++, Usage = usage });
    }

    private void Add(StreamEvent streamEvent) => _pending.Add(streamEvent);
}
