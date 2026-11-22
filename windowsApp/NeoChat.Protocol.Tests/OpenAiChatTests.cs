using System.Text;

namespace NeoChat.Protocol.Tests;

/// <summary>OAI-001..OAI-012: OpenAI Chat Completions mapping (spec/stream-events.md §4.1).</summary>
public sealed class OpenAiChatTests
{
    private static string Chunk(string deltaProperties, string? finishReason = null, string? trailingMembers = null)
    {
        var finish = finishReason is null ? "null" : "\"" + finishReason + "\"";
        var trailing = trailingMembers is null ? string.Empty : "," + trailingMembers;
        var choice = "{\"index\":0,\"delta\":{" + deltaProperties + "},\"finish_reason\":" + finish + "}";
        var payload = "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion.chunk\",\"created\":1730000000,\"model\":\"gpt-test\",\"choices\":[" + choice + "]" + trailing + "}";
        return "data: " + payload + "\n\n";
    }

    private static string Content(string text) => $"\"content\":{Harness.Quote(text)}";

    [Fact] // OAI-001
    public async Task Oai001_FiveContentChunks_ProduceFiveDeltasAndDone()
    {
        var body = Harness.Utf8(
            Chunk(Content("One "))
            + Chunk(Content("two "))
            + Chunk(Content("three "))
            + Chunk(Content("four "))
            + Chunk(Content("five"), "stop")
            + Harness.Done);

        var run = await Harness.RunAsync(body, pattern: "bytes:9");

        Assert.Equal(5, run.Events.Count(e => e.Kind == StreamEventKind.Delta));
        Assert.Equal("One two three four five", run.Text);
        Assert.Equal(6, run.Events.Count);
        run.AssertDone(FinishReason.Stop);
        int[] expectedSeq = [0, 1, 2, 3, 4, 5];
        Assert.Equal(expectedSeq, run.Events.Select(e => e.Seq));
    }

    [Fact] // OAI-002
    public async Task Oai002_UsageChunk_EmitsOneUsageBeforeDone()
    {
        var usage = "\"usage\":{\"prompt_tokens\":412,\"completion_tokens\":37,\"total_tokens\":449,\"prompt_tokens_details\":{\"cached_tokens\":128},\"completion_tokens_details\":{\"reasoning_tokens\":12}}";
        var body = Harness.Utf8(
            Chunk(Content("hello"))
            + Chunk(Content(" world"), "stop", usage)
            + Harness.Done);

        var run = await Harness.RunAsync(body, pattern: "random:3");

        var usageEvent = Assert.Single(run.Events, e => e.Kind == StreamEventKind.Usage);
        Assert.Equal(412, usageEvent.Usage!.Input);
        Assert.Equal(37, usageEvent.Usage!.Output);
        Assert.Equal(449, usageEvent.Usage!.Total);
        Assert.Equal(128, usageEvent.Usage!.Cached);
        Assert.Equal(12, usageEvent.Usage!.Reasoning);
        Assert.Equal(StreamEventKind.Done, run.Events[^1].Kind);
        Assert.Equal(usageEvent.Seq + 1, run.Events[^1].Seq);
        run.AssertDone(FinishReason.Stop);
    }

    [Fact] // OAI-003
    public async Task Oai003_SingleToolCallArgumentsInThreeFragments()
    {
        var body = Harness.Utf8(
            Chunk("\"tool_calls\":[{\"index\":0,\"id\":\"call_a1\",\"type\":\"function\",\"function\":{\"name\":\"web_search\",\"arguments\":\"\"}}]")
            + Chunk("\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"{\\\"q\\\":\"}}]")
            + Chunk("\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"\\\"天气\\\"}\"}}]")
            + Chunk(string.Empty, "tool_calls")
            + Harness.Done);

        var run = await Harness.RunAsync(body, pattern: "bytes:13");

        var deltas = run.Events.Where(e => e.Kind == StreamEventKind.ToolCallDelta).ToList();
        Assert.Equal(3, deltas.Count);
        Assert.Equal("call_a1", deltas[0].ToolCallId);
        Assert.Equal("web_search", deltas[0].ToolName);
        Assert.Equal(string.Empty, deltas[0].Args);
        Assert.Null(deltas[1].ToolName);
        Assert.Null(deltas[2].ToolName);
        Assert.Equal("{\"q\":\"天气\"}", string.Concat(deltas.Select(d => d.Args)));
        Assert.Single(run.Events, e => e.Kind == StreamEventKind.ToolCallEnd);
        run.AssertDone(FinishReason.ToolCalls);
    }

    [Fact] // OAI-004
    public async Task Oai004_TwoInterleavedToolCalls()
    {
        var body = Harness.Utf8(
            Chunk("\"tool_calls\":[{\"index\":0,\"id\":\"call_a\",\"function\":{\"name\":\"first\",\"arguments\":\"\"}},{\"index\":1,\"id\":\"call_b\",\"function\":{\"name\":\"second\",\"arguments\":\"\"}}]")
            + Chunk("\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"{\\\"a\\\":1}\"}}]")
            + Chunk("\"tool_calls\":[{\"index\":1,\"function\":{\"arguments\":\"{\\\"b\\\":2}\"}}]")
            + Chunk(string.Empty, "tool_calls")
            + Harness.Done);

        var run = await Harness.RunAsync(body, pattern: "random:11");

        var byIndex = run.Events
            .Where(e => e.Kind == StreamEventKind.ToolCallDelta)
            .GroupBy(e => e.Index!.Value)
            .ToDictionary(g => g.Key, g => string.Concat(g.Select(e => e.Args)));

        Assert.Equal("{\"a\":1}", byIndex[0]);
        Assert.Equal("{\"b\":2}", byIndex[1]);
        Assert.Equal(2, run.Events.Count(e => e.Kind == StreamEventKind.ToolCallEnd));
        run.AssertDone(FinishReason.ToolCalls);
    }

    [Fact] // OAI-005
    public async Task Oai005_ContentAndReasoningInOneDelta_DeltaComesFirst()
    {
        var body = Harness.Utf8(
            Chunk("\"reasoning_content\":\"先看用户意图\",\"content\":\"答案\"")
            + Chunk(string.Empty, "stop")
            + Harness.Done);

        var run = await Harness.RunAsync(body, pattern: "bytes:17");

        Assert.Equal(3, run.Events.Count);
        Assert.Equal(StreamEventKind.Delta, run.Events[0].Kind);
        Assert.Equal(StreamEventKind.ReasoningDelta, run.Events[1].Kind);
        Assert.Equal("答案", run.Text);
        Assert.Equal("先看用户意图", run.Reasoning);
        Assert.True(run.Events[1].Seq > run.Events[0].Seq);
    }

    [Fact] // OAI-005 (field priority)
    public async Task Oai005_ReasoningContentWinsOverReasoningAlias()
    {
        var body = Harness.Utf8(
            Chunk("\"reasoning_content\":\"首选\",\"reasoning\":\"备用\"")
            + Harness.Done);

        var run = await Harness.RunAsync(body);

        Assert.Equal("首选", run.Reasoning);
        Assert.Equal(1, run.Trace.ReasoningFieldConflicts);
    }

    [Fact] // OAI-006
    public async Task Oai006_FinishReasonLength()
    {
        var body = Harness.Utf8(Chunk(Content("截断"), "length") + Harness.Done);
        var run = await Harness.RunAsync(body);

        run.AssertDone(FinishReason.Length);
        Assert.Equal("length", run.Events[^1].RawFinishReason);
    }

    [Fact] // OAI-007
    public async Task Oai007_Http200WithJsonErrorBody_FailsWithQuotaCode()
    {
        var body = Harness.Utf8(
            "{\"error\":{\"message\":\"You exceeded your current quota, key sk-abcdefghijklmnopqrstuvwxyz012345\",\"type\":\"insufficient_quota\",\"code\":\"insufficient_quota\"}}");

        var run = await Harness.RunAsync(body, contentType: "application/json");

        var error = run.AssertFailed("AUTH_INSUFFICIENT_QUOTA");
        Assert.NotNull(error.Raw);
        Assert.True(Encoding.UTF8.GetByteCount(error.Raw!) <= 512, $"raw is {Encoding.UTF8.GetByteCount(error.Raw!)} bytes");
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz012345", error.Raw!, StringComparison.Ordinal);
        Assert.Contains("top_up", error.Actions.Select(a => a.A));
    }

    [Fact] // OAI-008
    public async Task Oai008_ContentFilterFinishReason_IsDoneNotFailed()
    {
        var body = Harness.Utf8(
            Chunk(Content("部分内容"))
            + Chunk(string.Empty, "content_filter")
            + Harness.Done);

        var run = await Harness.RunAsync(body, pattern: "bytes:7");

        Assert.Equal("部分内容", run.Text);
        run.AssertDone(FinishReason.ContentFilter);
    }

    [Fact] // OAI-009
    public async Task Oai009_DuplicateFlagDeduplicates_AndWithoutItBothDeltasSurvive()
    {
        var body = Harness.Utf8(
            Chunk(Content("重复"))
            + Chunk(Content("重复"))
            + Harness.Done);

        var deduped = await Harness.RunAsync(body, Harness.Options(duplicate: true), pattern: "bytes:5");
        Assert.Equal(2, deduped.Events.Count);
        Assert.Equal(0, deduped.Events[0].Seq);
        Assert.Equal(StreamEventKind.Delta, deduped.Events[0].Kind);
        Assert.Equal(1, deduped.Trace.DuplicateFramesSkipped);

        var plain = await Harness.RunAsync(body, pattern: "bytes:5");
        Assert.Equal(2, plain.Events.Count(e => e.Kind == StreamEventKind.Delta));
        Assert.Equal("重复重复", plain.Text);
    }

    [Fact] // OAI-010
    public async Task Oai010_OutOfOrderChunks_EmitInArrivalOrderWithMonotonicSeq()
    {
        var body = Harness.Utf8(
            Chunk(Content("3"))
            + Chunk(Content("1"))
            + Chunk(Content("2"), "stop")
            + Harness.Done);

        var run = await Harness.RunAsync(body, pattern: "bytes:6");

        Assert.Equal("312", run.Text);
        int[] expectedSeq = [0, 1, 2];
        Assert.Equal(expectedSeq, run.Events.Where(e => e.Kind == StreamEventKind.Delta).Select(e => e.Seq));
        run.AssertDone(FinishReason.Stop);
    }

    [Fact] // OAI-011
    public async Task Oai011_OneFrameWithContentAndTwoToolCalls_ThreeEventsInFixedOrder()
    {
        var body = Harness.Utf8(
            Chunk("\"content\":\"好的\",\"tool_calls\":[{\"index\":1,\"id\":\"call_b\",\"function\":{\"name\":\"second\",\"arguments\":\"{}\"}},{\"index\":0,\"id\":\"call_a\",\"function\":{\"name\":\"first\",\"arguments\":\"{}\"}}]")
            + Chunk(string.Empty, "tool_calls")
            + Harness.Done);

        var run = await Harness.RunAsync(body, pattern: "bytes:23");

        var firstThree = run.Events.Take(3).ToList();
        Assert.Equal(StreamEventKind.Delta, firstThree[0].Kind);
        Assert.Equal(StreamEventKind.ToolCallDelta, firstThree[1].Kind);
        Assert.Equal(0, firstThree[1].Index);
        Assert.Equal(StreamEventKind.ToolCallDelta, firstThree[2].Kind);
        Assert.Equal(1, firstThree[2].Index);
        int[] expectedSeq = [0, 1, 2];
        Assert.Equal(expectedSeq, firstThree.Select(e => e.Seq));
        run.AssertDone(FinishReason.ToolCalls);
    }

    [Fact] // OAI-012
    public async Task Oai012_CumulativeEndpoint_EmitsOnlyTheIncrement()
    {
        var body = Harness.Utf8(
            Chunk(Content("你"))
            + Chunk(Content("你好"))
            + Chunk(Content("你好，世界"))
            + Chunk(string.Empty, "stop")
            + Harness.Done);

        var run = await Harness.RunAsync(body, Harness.Options(cumulative: true), pattern: "bytes:8");

        var deltas = run.Events.Where(e => e.Kind == StreamEventKind.Delta).ToList();
        Assert.Equal(3, deltas.Count);
        Assert.Equal("你", deltas[0].Text);
        Assert.Equal("好", deltas[1].Text);
        Assert.Equal("，世界", deltas[2].Text);
        Assert.Equal("你好，世界", run.Text);
        run.AssertDone(FinishReason.Stop);
    }
}
