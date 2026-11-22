using System.Text;
using NeoChat.Protocol.Diagnostics;
using NeoChat.Protocol.Events;
using NeoChat.Protocol.Pipeline;

namespace NeoChat.Protocol.Tests.Support;

public sealed record StreamRun(IReadOnlyList<StreamEvent> Events, StreamTrace Trace, bool BodyDisposed, string Jsonl)
{
    public static StreamRun From(IReadOnlyList<StreamEvent> events, StreamTrace trace, bool bodyDisposed)
    {
        var jsonl = StreamEventJson.ToJsonl(events);
        return new StreamRun(events, trace, bodyDisposed, jsonl);
    }

    public string Sha256 => StreamEventJson.Sha256Hex(Jsonl);

    public StreamEvent Terminal => Events[^1];

    public string Text => string.Concat(Events.Where(e => e.Kind == StreamEventKind.Delta).Select(e => e.Text));

    public string Reasoning => string.Concat(Events.Where(e => e.Kind == StreamEventKind.ReasoningDelta).Select(e => e.Text));

    public ProviderError AssertFailed(string expectedCode)
    {
        var terminal = Terminal;
        Assert.Equal(StreamEventKind.Failed, terminal.Kind);
        Assert.NotNull(terminal.Error);
        Assert.Equal(expectedCode, terminal.Error!.Code);
        AssertInvariants();
        return terminal.Error;
    }

    public StreamEvent AssertDone(FinishReason expected)
    {
        var terminal = Terminal;
        Assert.Equal(StreamEventKind.Done, terminal.Kind);
        Assert.Equal(expected, terminal.FinishReason);
        AssertInvariants();
        return terminal;
    }

    public void AssertInvariants()
    {
        for (var i = 0; i < Events.Count; i++)
        {
            Assert.Equal(i, Events[i].Seq);
        }

        Assert.True(Events.Count > 0, "a stream must always end with a terminal event");
        Assert.Single(Events, e => e.IsTerminal);
        Assert.True(Events[^1].IsTerminal, "the terminal event must be last");

        foreach (var delta in Events.Where(e => e.Kind is StreamEventKind.Delta or StreamEventKind.ReasoningDelta))
        {
            Assert.False(string.IsNullOrEmpty(delta.Text), "I6: empty text must never be emitted");
        }

        var usage = Events.Where(e => e.Kind == StreamEventKind.Usage).ToList();
        Assert.True(usage.Count <= 1, "I4: at most one usage event");
        if (usage.Count == 1)
        {
            Assert.True(usage[0].Seq < Events[^1].Seq, "I4: usage precedes the terminal event");
        }

        var opened = Events.Where(e => e.Kind == StreamEventKind.ToolCallDelta).Select(e => e.Index!.Value).Distinct().Order().ToList();
        var ended = Events.Where(e => e.Kind == StreamEventKind.ToolCallEnd).Select(e => e.Index!.Value).Order().ToList();
        Assert.Equal(opened, ended);
        foreach (var end in Events.Where(e => e.Kind == StreamEventKind.ToolCallEnd))
        {
            Assert.True(end.Seq < Events[^1].Seq, "I5: tool_call_end precedes the terminal event");
        }

        if (Events[^1].Kind == StreamEventKind.Done && Events[^1].FinishReason == FinishReason.ToolCalls)
        {
            Assert.NotEmpty(opened);
        }
    }
}

/// <summary>Drives one sample through a <see cref="ChatStreamSession"/> and collects every event.</summary>
public static class Harness
{
    public const string EventStreamContentType = "text/event-stream; charset=utf-8";

    public static ChatStreamOptions Options(
        string? model = "gpt-test",
        bool cumulative = false,
        bool duplicate = false,
        TimeSpan? readIdle = null,
        int maxPayloadBytes = Transport.SseFramer.DefaultMaxPayloadBytes) => new()
    {
        Protocol = ProtocolFamily.OpenAiChat,
        Provider = "openai_compatible",
        Model = model ?? "gpt-test",
        CumulativeStream = cumulative,
        DuplicateFrames = duplicate,
        ReadIdleTimeout = readIdle ?? TimeSpan.FromSeconds(180),
        MaxPayloadBytes = maxPayloadBytes,
    };

    /// <summary>Splits a sample per the runner's <c>chunk_pattern</c> grammar (CASES.md §0).</summary>
    public static IReadOnlyList<int> ChunkSizes(int totalBytes, string pattern)
    {
        var sizes = new List<int>();
        if (pattern is "whole" || string.IsNullOrEmpty(pattern))
        {
            sizes.Add(totalBytes);
            return sizes;
        }

        var parts = pattern.Split(':', 2);
        var kind = parts[0];
        var argument = parts.Length > 1 ? parts[1] : "0";
        switch (kind)
        {
            case "bytes":
            case "fixed":
            {
                var size = int.Parse(argument, System.Globalization.CultureInfo.InvariantCulture);
                for (var sent = 0; sent < totalBytes; sent += size)
                {
                    sizes.Add(Math.Min(size, totalBytes - sent));
                }

                return sizes;
            }

            case "random":
            {
                var random = new Random(int.Parse(argument, System.Globalization.CultureInfo.InvariantCulture));
                for (var sent = 0; sent < totalBytes;)
                {
                    var size = Math.Min(random.Next(1, 65), totalBytes - sent);
                    sizes.Add(size);
                    sent += size;
                }

                return sizes;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(pattern), pattern, "unknown chunk pattern");
        }
    }

    public static Task<StreamRun> RunAsync(
        byte[] body,
        ChatStreamOptions? options = null,
        string? contentType = EventStreamContentType,
        int statusCode = 200,
        IReadOnlyDictionary<string, string>? headers = null,
        string pattern = "whole") =>
        RunSizedAsync(body, ChunkSizes(body.Length, pattern), options, contentType, statusCode, headers);

    /// <summary>Feeds the sample with explicit chunk sizes, for cuts that must land at a chosen offset.</summary>
    public static async Task<StreamRun> RunSizedAsync(
        byte[] body,
        IReadOnlyList<int> sizes,
        ChatStreamOptions? options = null,
        string? contentType = EventStreamContentType,
        int statusCode = 200,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var chunked = new ChunkedStream(body, sizes);
        var stream = ChatStreamSession.Start(
            chunked,
            new StreamResponseInfo { StatusCode = statusCode, ContentType = contentType, Headers = headers },
            options ?? Options());
        var events = new List<StreamEvent>();
        await foreach (var streamEvent in stream.Events())
        {
            events.Add(streamEvent);
        }

        return StreamRun.From(events, stream.Trace, chunked.Disposed);
    }

    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>A chat-completions frame with one content chunk.</summary>
    public static string ContentFrame(string content, string? finishReason = null, string? extraJson = null)
    {
        var finish = finishReason is null ? "null" : $"\"{finishReason}\"";
        var usage = extraJson is null ? string.Empty : $",{extraJson}";
        return $"data: {{\"id\":\"chunk\",\"object\":\"chat.completion.chunk\",\"choices\":[{{\"index\":0,\"delta\":{{\"content\":{Quote(content)}}},\"finish_reason\":{finish}}}]}}{usage}\n\n";
    }

    public static string Quote(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    public static string Frame(string payload) => $"data: {payload}\n\n";

    public const string Done = "data: [DONE]\n\n";
}

internal static class ChatStreamExtensions
{
    /// <summary>Convenience so tests can write <c>await foreach (var e in stream.Events())</c>.</summary>
    public static IAsyncEnumerable<StreamEvent> Events(this ChatStreamSession stream) => stream;
}
