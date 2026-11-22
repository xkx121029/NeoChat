using Xunit.Abstractions;

namespace NeoChat.Protocol.Tests;

/// <summary>
/// The G1 criterion of spec/stream-events.md §6: the same bytes must yield the same event stream
/// however the transport happens to slice them. Three feeds: whole, one byte at a time, random:7.
/// </summary>
public sealed class ByteSplitEquivalenceTests
{
    private readonly ITestOutputHelper _output;

    public ByteSplitEquivalenceTests(ITestOutputHelper output) => _output = output;

    private static byte[] Sample()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("data: {\"id\":\"c1\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"中文，emoji🙂 与非 BMP 𝄞\"},\"finish_reason\":null}]}\n\n");
        sb.Append(": OPENROUTER PROCESSING\n\n");
        sb.Append("data: {\"id\":\"c2\",\"choices\":[{\"index\":0,\"delta\":{\"reasoning_content\":\"先想一下\"},\"finish_reason\":null}]}\n\n");
        sb.Append("data: {\"id\":\"c3\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"\",\"tool_calls\":[{\"index\":0,\"id\":\"call_0\",\"function\":{\"name\":\"web_search\",\"arguments\":\"\"}}]},\"finish_reason\":null}]}\n\n");
        sb.Append("data: {\"id\":\"c4\",\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"{\\\"q\\\":\"}}]},\"finish_reason\":null}]}\n\n");
        sb.Append("data: {\"id\":\"c5\",\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"\\\"天气\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}\n\n");
        sb.Append("data: {\"id\":\"c6\",\"choices\":[],\"usage\":{\"prompt_tokens\":412,\"completion_tokens\":37,\"total_tokens\":449,\"prompt_tokens_details\":{\"cached_tokens\":0}}}\n\n");
        sb.Append("data: [DONE]\n\n");
        return Harness.Utf8(sb.ToString());
    }

    [Fact]
    public async Task Whole_ByteWise_AndRandomChunks_ProduceIdenticalEventJson()
    {
        var body = Sample();

        var whole = await Harness.RunAsync(body, pattern: "whole");
        var byteWise = await Harness.RunAsync(body, pattern: "bytes:1");
        var random = await Harness.RunAsync(body, pattern: "random:7");

        _output.WriteLine($"whole    events={whole.Events.Count}  sha256={whole.Sha256}");
        _output.WriteLine($"bytes:1  events={byteWise.Events.Count}  sha256={byteWise.Sha256}");
        _output.WriteLine($"random:7 events={random.Events.Count}  sha256={random.Sha256}");
        _output.WriteLine("events.jsonl:");
        _output.WriteLine(whole.Jsonl.TrimEnd('\n'));

        Assert.Equal(whole.Events.Count, byteWise.Events.Count);
        Assert.Equal(whole.Events.Count, random.Events.Count);
        Assert.Equal(whole.Jsonl, byteWise.Jsonl);
        Assert.Equal(whole.Jsonl, random.Jsonl);
        Assert.Equal(whole.Sha256, byteWise.Sha256);
        Assert.Equal(whole.Sha256, random.Sha256);
        whole.AssertDone(FinishReason.ToolCalls);
    }
}
