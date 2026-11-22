using System.Runtime.CompilerServices;
using NeoChat.Protocol.Canonical;
using NeoChat.Protocol.Events;
using NeoChat.Windows.Core.Chat;

namespace NeoChat.Windows.Core.Tests;

/// <summary>Replays a fixed event script per attempt, optionally holding the stream open until cancel.</summary>
internal sealed class ScriptedChatStreamClient : IChatStreamClient
{
    private readonly Queue<Script> _scripts = new();

    public List<ChatRequest> Requests { get; } = [];

    public int Attempts { get; private set; }

    public ChatRequest? LastRequest => Requests.Count == 0 ? null : Requests[^1];

    public ScriptedChatStreamClient Then(params StreamEvent[] events) => Enqueue(events, false);

    public ScriptedChatStreamClient ThenThrow(Exception failure) => Enqueue([], false, null, failure);

    public ScriptedChatStreamClient ThenWaitForCancel(out Func<Task> whenDelivered, params StreamEvent[] events)
    {
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        whenDelivered = () => delivered.Task;
        return Enqueue(events, true, delivered);
    }

    public async IAsyncEnumerable<StreamEvent> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Attempts++;
        var script = _scripts.Count > 0 ? _scripts.Dequeue() : new Script([], false, null, null);
        if (script.Failure is not null)
        {
            throw script.Failure;
        }

        foreach (var streamEvent in script.Events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return streamEvent;
            await Task.Yield();
        }

        script.Delivered?.TrySetResult();
        if (script.WaitForCancel)
        {
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(() => cancelled.TrySetResult());
            await cancelled.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private ScriptedChatStreamClient Enqueue(StreamEvent[] events, bool waitForCancel, TaskCompletionSource? delivered = null, Exception? failure = null)
    {
        _scripts.Enqueue(new Script(events, waitForCancel, delivered, failure));
        return this;
    }

    private sealed record Script(StreamEvent[] Events, bool WaitForCancel, TaskCompletionSource? Delivered, Exception? Failure);
}

internal static class Events
{
    public static StreamEvent Delta(string text, int seq) =>
        new() { Kind = StreamEventKind.Delta, Seq = seq, Text = text };

    public static StreamEvent Reasoning(string text, int seq) =>
        new() { Kind = StreamEventKind.ReasoningDelta, Seq = seq, Text = text };

    public static StreamEvent Usage(int seq, long input, long output) =>
        new() { Kind = StreamEventKind.Usage, Seq = seq, Usage = new UsageInfo(input, output) };

    public static StreamEvent Done(int seq, FinishReason reason = FinishReason.Stop) =>
        new() { Kind = StreamEventKind.Done, Seq = seq, FinishReason = reason };

    public static StreamEvent Failed(int seq, ProviderError error) =>
        new() { Kind = StreamEventKind.Failed, Seq = seq, Error = error };
}
