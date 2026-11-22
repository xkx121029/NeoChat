using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using NeoChat.Protocol.Errors;

namespace NeoChat.Protocol.Tests;

/// <summary>FRAME-001..FRAME-013: the L1 framing layer of spec/stream-events.md §2.</summary>
public sealed class SseFramingTests
{
    [Fact] // FRAME-001
    public async Task Frame001_ByteWiseFeedMatchesWholeFeed_ForCjkAndNonBmp()
    {
        var body = Harness.Utf8(
            Harness.ContentFrame("你好，")
            + Harness.ContentFrame("世界🙂")
            + Harness.ContentFrame("𝄞结束")
            + Harness.Done);

        var whole = await Harness.RunAsync(body, pattern: "whole");
        var byteWise = await Harness.RunAsync(body, pattern: "bytes:1");

        Assert.Equal("你好，世界🙂𝄞结束", whole.Text);
        Assert.Equal(whole.Jsonl, byteWise.Jsonl);
        Assert.Equal(whole.Sha256, byteWise.Sha256);
        whole.AssertDone(FinishReason.Stop);
    }

    [Fact] // FRAME-002
    public async Task Frame002_SingleDataEventSplitInThree_InsideJsonString()
    {
        var body = Harness.Utf8(Harness.ContentFrame("切点在字符串内部🙂", "stop") + Harness.Done);
        var whole = await Harness.RunAsync(body, pattern: "whole");

        // Cuts land inside the JSON string value, and inside a multi-byte UTF-8 character is
        // covered by FRAME-001, so here the split is exactly three segments of the one frame.
        const int cut1 = 36;
        var cut2 = cut1 + 9;
        Assert.True(cut2 < body.Length, "the frame must be longer than the chosen cuts");
        var three = await Harness.RunSizedAsync(body, [cut1, cut2 - cut1, body.Length - cut2]);

        Assert.Equal(whole.Jsonl, three.Jsonl);
        Assert.Single(three.Events, e => e.Kind == StreamEventKind.Delta);
        Assert.Equal("切点在字符串内部🙂", three.Text);
    }

    [Fact] // FRAME-003
    public async Task Frame003_HeartbeatsProduceNoEvents_AndResetIdle()
    {
        var withHeartbeats = new StringBuilder();
        for (var i = 0; i < 5; i++)
        {
            withHeartbeats.Append(Harness.ContentFrame(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            withHeartbeats.Append(": keep-alive\n\n");
            withHeartbeats.Append(": OPENROUTER PROCESSING\n\n");
        }

        withHeartbeats.Append(Harness.Done);

        var plain = new StringBuilder();
        for (var i = 0; i < 5; i++)
        {
            plain.Append(Harness.ContentFrame(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        plain.Append(Harness.Done);

        var withHearts = await Harness.RunAsync(Harness.Utf8(withHeartbeats.ToString()), pattern: "bytes:7");
        var withoutHearts = await Harness.RunAsync(Harness.Utf8(plain.ToString()), pattern: "bytes:7");

        Assert.Equal(withoutHearts.Jsonl, withHearts.Jsonl);
        Assert.True(withHearts.Trace.HeartbeatLines >= 10, $"heartbeat lines counted: {withHearts.Trace.HeartbeatLines}");
        Assert.DoesNotContain(withHearts.Events, e => e.Kind == StreamEventKind.Notice);
    }

    [Fact] // FRAME-003 (idle reset, real socket and real clock)
    public async Task Frame003_HeartbeatsKeepTheStreamAlive_PastTheIdleWindow()
    {
        var counter = new ActiveStreamCounter();
        await using var server = RawHttpServer.Start(async (srv, stream, _, ct) =>
        {
            await srv.WriteHeadAsync(stream, ct);
            await srv.WriteAsync(stream, Harness.ContentFrame("a"), ct);
            for (var i = 0; i < 6; i++)
            {
                await Task.Delay(200, ct);
                await srv.WriteAsync(stream, ": keep-alive\n\n", ct);
            }

            await srv.WriteAsync(stream, Harness.Done, ct);
        });

        var (client, chat, _) = await RawHttpServer.ConnectAsync(server.Url, Harness.Options(readIdle: TimeSpan.FromMilliseconds(600)), counter);
        using (client)
        {
            var events = new List<StreamEvent>();
            await foreach (var streamEvent in chat)
            {
                events.Add(streamEvent);
            }

            Assert.Equal("a", StreamRun.From(events, chat.Trace, false).Text);
            Assert.Equal(StreamEventKind.Done, events[^1].Kind);
            Assert.True(chat.Trace.HeartbeatLines >= 6);
        }
    }

    [Fact] // FRAME-004
    public async Task Frame004_MissingDoneMarker_WithFinishReason_EndsWithDone()
    {
        var body = Harness.Utf8(Harness.ContentFrame("前半", "stop") + Harness.ContentFrame("后半"));
        var run = await Harness.RunAsync(body, pattern: "bytes:5");

        run.AssertDone(FinishReason.Stop);
        Assert.Equal("前半后半", run.Text);
    }

    [Fact] // FRAME-005
    public async Task Frame005_DoneInTheMiddle_StopsTheStreamThere()
    {
        var body = Harness.Utf8(
            Harness.ContentFrame("a")
            + Harness.ContentFrame("b")
            + Harness.Done
            + Harness.ContentFrame("c")
            + Harness.ContentFrame("d")
            + Harness.ContentFrame("e"));

        var run = await Harness.RunAsync(body, pattern: "bytes:3");

        Assert.Equal("ab", run.Text);
        Assert.Equal(3, run.Events.Count);
        run.AssertDone(FinishReason.Stop);
        Assert.Equal(2, run.Events[^1].Seq);
    }

    [Fact] // FRAME-006
    public async Task Frame006_RepeatedDoneMarker_YieldsOneDone()
    {
        var body = Harness.Utf8(Harness.ContentFrame("x") + Harness.Done + Harness.Done + Harness.Done);
        var run = await Harness.RunAsync(body, pattern: "bytes:4");

        Assert.Single(run.Events, e => e.Kind == StreamEventKind.Done);
        run.AssertDone(FinishReason.Stop);
    }

    [Fact] // FRAME-007
    public async Task Frame007_MixedLineEndings_LeaveNoCarriageReturnInPayload()
    {
        static string Payload(string text) =>
            $"{{\"choices\":[{{\"index\":0,\"delta\":{{\"content\":\"{text}\"}},\"finish_reason\":null}}]}}";

        var body = Harness.Utf8(
            "data: " + Payload("crlf") + "\r\n\r\n"
            + "data: " + Payload("cr") + "\r\r"
            + "data: " + Payload("lf") + "\n\n"
            + "data: " + Payload("mixed") + "\r\n\r"
            + Harness.Done);

        var run = await Harness.RunAsync(body, pattern: "bytes:2");

        Assert.Equal("crlfcrlfmixed", run.Text);
        Assert.False(run.Text.Contains('\r', StringComparison.Ordinal));
        run.AssertDone(FinishReason.Stop);
    }

    [Fact] // FRAME-008
    public async Task Frame008_BomDropped_InvalidBytesReplaced_NoThrow()
    {
        var head = new byte[] { 0xEF, 0xBB, 0xBF };
        var prefix = Harness.Utf8("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"a");
        var suffix = Harness.Utf8("b\"},\"finish_reason\":null}]}\n\n" + Harness.Done);
        var body = new byte[head.Length + prefix.Length + 2 + suffix.Length];
        var offset = 0;
        head.CopyTo(body, offset);
        offset += head.Length;
        prefix.CopyTo(body, offset);
        offset += prefix.Length;
        body[offset++] = 0xFF;
        body[offset++] = 0xFE;
        suffix.CopyTo(body, offset);

        var run = await Harness.RunAsync(body, pattern: "bytes:1");

        Assert.Contains("\uFFFD", run.Text, StringComparison.Ordinal);
        Assert.Equal("a", run.Text[..1]);
        Assert.Equal("b", run.Text[^1..]);
        Assert.True(run.Trace.InvalidUtf8Bytes >= 2, $"invalid_utf8_bytes={run.Trace.InvalidUtf8Bytes}");
        run.AssertDone(FinishReason.Stop);
    }

    [Fact] // FRAME-009
    public async Task Frame009_SingleTenMebibyteDataLine_IsAccepted()
    {
        var big = new string('a', 10 * 1024 * 1024);
        var body = Harness.Utf8(Harness.ContentFrame(big, "stop") + Harness.Done);

        var run = await Harness.RunAsync(body, pattern: "bytes:65536");

        var delta = Assert.Single(run.Events, e => e.Kind == StreamEventKind.Delta);
        Assert.Equal(big.Length, delta.Text!.Length);
        run.AssertDone(FinishReason.Stop);
        Assert.True(run.Trace.InvalidUtf8Bytes == 0);
    }

    [Fact] // FRAME-010
    public async Task Frame010_TwentyMebibyteFrame_FailsAndClosesTheBody()
    {
        var huge = new string('b', 20 * 1024 * 1024);
        var body = Harness.Utf8(Harness.ContentFrame(huge, "stop") + Harness.Done);

        var run = await Harness.RunAsync(body, pattern: "bytes:65536");

        run.AssertFailed(ErrorCatalog.ParseEventTooLarge);
        Assert.True(run.BodyDisposed, "the body must be closed after PARSE_EVENT_TOO_LARGE (spec §2.1 rule 10)");
    }

    [Fact] // FRAME-011
    public async Task Frame011_EmptyContentDeltas_ProduceNoEvents()
    {
        var body = Harness.Utf8(
            Harness.ContentFrame(string.Empty)
            + Harness.ContentFrame("甲")
            + Harness.ContentFrame(string.Empty)
            + Harness.ContentFrame(string.Empty)
            + Harness.ContentFrame("乙")
            + Harness.Done);

        var run = await Harness.RunAsync(body, pattern: "bytes:11");

        Assert.Equal("甲乙", run.Text);
        Assert.Equal(2, run.Events.Count(e => e.Kind == StreamEventKind.Delta));
        Assert.DoesNotContain(run.Events, e => e.Kind is StreamEventKind.Delta && string.IsNullOrEmpty(e.Text));
    }

    [Fact] // FRAME-012
    public async Task Frame012_ConnectionHeldOpenAfterDone_IsReleasedWithin100Ms()
    {
        var counter = new ActiveStreamCounter();
        await using var server = RawHttpServer.Start(async (srv, stream, _, ct) =>
        {
            await srv.WriteHeadAsync(stream, ct);
            await srv.WriteAsync(stream, Harness.ContentFrame("ok", "stop") + Harness.Done, ct);
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(100, ct);
                await srv.WriteAsync(stream, ": held-open\n\n", ct);
            }
        });

        var (client, chat, _) = await RawHttpServer.ConnectAsync(server.Url, Harness.Options(), counter);
        using (client)
        {
            var events = new List<StreamEvent>();
            var terminalAt = 0L;
            await foreach (var streamEvent in chat)
            {
                events.Add(streamEvent);
                if (streamEvent.IsTerminal)
                {
                    terminalAt = Stopwatch.GetTimestamp();
                    break;
                }
            }

            var releasedMs = (Stopwatch.GetTimestamp() - terminalAt) * 1000.0 / Stopwatch.Frequency;
            Assert.Equal(StreamEventKind.Done, events[^1].Kind);
            Assert.Equal(0, counter.Count);
            Assert.True(releasedMs < 100, $"the stream stayed active {releasedMs:F1} ms after the terminal event (I12 allows 100)");
            await server.ClientClosed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    [Fact] // FRAME-013
    public async Task Frame013_GzipBody_MatchesPlainBodyByteForByte()
    {
        var plain = Harness.Utf8(
            Harness.ContentFrame("压缩前🙂")
            + Harness.ContentFrame("压缩后")
            + Harness.ContentFrame(string.Empty)
            + Harness.Done);

        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(plain);
        }

        await using var decompressed = new MemoryStream();
        using (var gunzip = new GZipStream(new MemoryStream(buffer.ToArray()), CompressionMode.Decompress))
        {
            await gunzip.CopyToAsync(decompressed);
        }

        var plainRun = await Harness.RunAsync(plain, pattern: "bytes:5");
        var gzipRun = await Harness.RunAsync(decompressed.ToArray(), pattern: "bytes:5");

        Assert.Equal(plainRun.Jsonl, gzipRun.Jsonl);
        Assert.Equal(plainRun.Sha256, gzipRun.Sha256);
    }
}
