using NeoChat.Protocol.Normalize;
using NeoChat.Protocol.Transport;

namespace NeoChat.Protocol.Tests;

/// <summary>One dedicated test per invariant the task calls out: I6, I8, I11, I12 (plus I1/I2 sweeps).</summary>
public sealed class InvariantTests
{
    [Fact] // I6
    public async Task I6_EmptyDeltaText_IsNeverEmitted()
    {
        var body = Harness.Utf8(
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"\"}}]}\n\n"
            + "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":null}}]}\n\n"
            + "data: {\"choices\":[{\"index\":0,\"delta\":{\"reasoning_content\":\"\"}}]}\n\n"
            + "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\" \"}}]}\n\n"
            + "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"实\"},\"finish_reason\":\"stop\"}]}\n\n"
            + Harness.Done);

        var run = await Harness.RunAsync(body, pattern: "bytes:3");

        Assert.All(run.Events, e => Assert.NotEqual(StreamEventKind.ReasoningDelta, e.Kind));
        var deltas = run.Events.Where(e => e.Kind == StreamEventKind.Delta).Select(e => e.Text).ToList();
        string[] expectedTexts = [" ", "实"];
        Assert.Equal(expectedTexts, deltas);
        run.AssertDone(FinishReason.Stop);
    }

    [Fact] // I8
    public async Task I8_OneFrameYieldsThreeEvents_ContiguousSeq_NoHalfJson()
    {
        var body = Harness.Utf8(
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"甲\",\"tool_calls\":[{\"index\":0,\"id\":\"call_0\",\"function\":{\"name\":\"f\",\"arguments\":\"{\\\"a\\\":1}\"}},{\"index\":1,\"id\":\"call_1\",\"function\":{\"name\":\"g\",\"arguments\":\"{}\"}}]},\"finish_reason\":\"tool_calls\"}]}\n\n"
            + Harness.Done);

        var run = await Harness.RunAsync(body, pattern: "bytes:61");

        Assert.Equal(5, run.Events.Count(e => !e.IsTerminal));
        int[] expectedSeq = [0, 1, 2];
        Assert.Equal(expectedSeq, run.Events.Take(3).Select(e => e.Seq));
        string[] expectedPayloads = ["甲", "{\"a\":1}", "{}"];
        Assert.Equal(expectedPayloads, run.Events.Take(3).Select(e => e.Text ?? e.Args));
        run.AssertDone(FinishReason.ToolCalls);
    }

    [Fact] // I11
    public async Task I11_CumulativeText_OnlyTheIncrementIsEmitted()
    {
        var body = Harness.Utf8(
            Harness.Frame("{\"choices\":[{\"index\":0,\"delta\":{\"content\":\"前言：\"}}]}")
            + Harness.Frame("{\"choices\":[{\"index\":0,\"delta\":{\"content\":\"前言：正文\"}}]}")
            + Harness.Frame("{\"choices\":[{\"index\":0,\"delta\":{\"content\":\"前言：正文完\"}}]}")
            + Harness.Frame("{\"choices\":[{\"index\":0,\"delta\":{\"content\":\"完全重写\"}}]}")
            + Harness.Done);

        var run = await Harness.RunAsync(body, Harness.Options(cumulative: true), pattern: "bytes:13");

        Assert.Equal("前言：", run.Events[0].Text);
        Assert.Equal("正文", run.Events[1].Text);
        Assert.Equal("完", run.Events[2].Text);
        Assert.Equal("前言：正文完", run.Text);
        Assert.Equal(3, run.Events.Count(e => e.Kind == StreamEventKind.Delta));
        Assert.Equal(1, run.Trace.RewritesIgnored);
        run.AssertDone(FinishReason.Stop);
    }

    [Fact] // I12
    public async Task I12_TerminalEventReleasesTheBody()
    {
        var counter = new ActiveStreamCounter();
        var body = Harness.Utf8(Harness.ContentFrame("x", "stop") + Harness.Done + Harness.ContentFrame("ignored"));
        var chunked = new ChunkedStream(body, Harness.ChunkSizes(body.Length, "bytes:4"));
        var stream = ChatStreamSession.Start(
            chunked,
            new StreamResponseInfo { StatusCode = 200, ContentType = Harness.EventStreamContentType },
            Harness.Options(),
            counter);

        var terminal = default(StreamEvent);
        var sawActiveStream = false;
        await foreach (var streamEvent in stream)
        {
            sawActiveStream |= counter.Count == 1;
            if (streamEvent.IsTerminal)
            {
                terminal = streamEvent;
            }
        }

        Assert.True(sawActiveStream, "the counter must be 1 while the stream is open");
        Assert.Equal(StreamEventKind.Done, terminal!.Kind);
        Assert.Equal(0, counter.Count);
        Assert.True(chunked.Disposed, "the body must be disposed once the terminal event is out");
    }

    [Fact] // I1 + I2 sweep over every sample shape used by the suite
    public async Task I1AndI2_SeqStartsAtZeroAndOnlyOneTerminal()
    {
        var body = Harness.Utf8(
            Harness.ContentFrame("a")
            + Harness.ContentFrame("b", "stop")
            + Harness.Frame("{\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":2,\"total_tokens\":3}}")
            + Harness.Done);

        var run = await Harness.RunAsync(body, pattern: "random:19");

        Assert.Equal(Enumerable.Range(0, run.Events.Count), run.Events.Select(e => e.Seq));
        Assert.Single(run.Events, e => e.IsTerminal);
        run.AssertDone(FinishReason.Stop);
    }

    [Fact]
    public void EmitterSynthesizesMissingToolCallId()
    {
        var emitter = new StreamEventEmitter(new NeoChat.Protocol.Diagnostics.StreamTrace());
        emitter.ToolCallDelta(2, id: null, name: "web_search", args: "{}");
        var emitted = emitter.Drain();

        Assert.Single(emitted);
        Assert.Equal("call_2", emitted[0].ToolCallId);
    }

    [Fact]
    public void EmitterRefusesThreeEventOrderViolations()
    {
        var emitter = new StreamEventEmitter(new NeoChat.Protocol.Diagnostics.StreamTrace());
        emitter.ToolCallDelta(0, "call_a", "f", string.Empty);
        emitter.Done(FinishReason.ToolCalls);

        var emitted = emitter.Drain();
        Assert.Equal(
            new[] { StreamEventKind.ToolCallDelta, StreamEventKind.ToolCallEnd, StreamEventKind.Done },
            emitted.Select(e => e.Kind));

        emitter.Delta("after terminal");
        Assert.Empty(emitter.Drain());
    }
}
