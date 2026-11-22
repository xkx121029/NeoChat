using System.Net.Sockets;

namespace NeoChat.Protocol.Tests;

/// <summary>
/// CAN-001 / CAN-002 shaped cases: cancellation must close the underlying connection (not merely
/// stop reading) and must end with <c>done{aborted_user}</c> (spec §5.1, I10).
/// </summary>
public sealed class CancellationTests
{
    [Fact]
    public async Task CancelBeforeFirstByte_ClosesTheSocket_AndEndsWithAbortedUser()
    {
        var counter = new ActiveStreamCounter();
        await using var server = RawHttpServer.Start(async (srv, stream, _, ct) =>
        {
            await srv.WriteHeadAsync(stream, ct);
            await Task.Delay(400, ct);
            while (!ct.IsCancellationRequested)
            {
                await srv.WriteAsync(stream, Harness.ContentFrame("late"), ct);
                await Task.Delay(100, ct);
            }
        });

        var (client, chat, _) = await RawHttpServer.ConnectAsync(server.Url, Harness.Options(), counter);
        using (client)
        {
            var events = new List<StreamEvent>();
            var consumer = Task.Run(async () =>
            {
                await foreach (var streamEvent in chat)
                {
                    events.Add(streamEvent);
                }
            });

            await server.Connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            chat.Cancel();
            await consumer.WaitAsync(TimeSpan.FromSeconds(5));
            await server.ClientClosed.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Single(events);
            Assert.Equal(StreamEventKind.Done, events[0].Kind);
            Assert.Equal(FinishReason.AbortedUser, events[0].FinishReason);
            Assert.DoesNotContain(events, e => e.Kind == StreamEventKind.Failed);
            Assert.Equal(0, counter.Count);
        }
    }

    [Fact]
    public async Task CancelAfterThreeDeltas_KeepsContent_ClosesTheSocket_AndAppendsDone()
    {
        var counter = new ActiveStreamCounter();
        await using var server = RawHttpServer.Start(async (srv, stream, _, ct) =>
        {
            await srv.WriteHeadAsync(stream, ct);
            while (!ct.IsCancellationRequested)
            {
                await srv.WriteAsync(stream, Harness.ContentFrame("内容"), ct);
                await Task.Delay(20, ct);
            }
        });

        var (client, chat, _) = await RawHttpServer.ConnectAsync(server.Url, Harness.Options(), counter);
        using (client)
        {
            var events = new List<StreamEvent>();
            await foreach (var streamEvent in chat)
            {
                events.Add(streamEvent);
                if (events.Count(e => e.Kind == StreamEventKind.Delta) == 3)
                {
                    chat.Cancel();
                }
            }

            await server.ClientClosed.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(3, events.Count(e => e.Kind == StreamEventKind.Delta));
            Assert.Equal(StreamEventKind.Done, events[^1].Kind);
            Assert.Equal(FinishReason.AbortedUser, events[^1].FinishReason);
            Assert.DoesNotContain(events, e => e.Kind == StreamEventKind.Failed);
            Assert.Equal(0, counter.Count);
            StreamRun.From(events, chat.Trace, false).AssertInvariants();
        }
    }

    [Fact]
    public async Task CancelIsIdempotent_AndDoesNotEmitASecondTerminal()
    {
        var body = Harness.Utf8(Harness.ContentFrame("x") + Harness.Done);
        var stream = ChatStreamSession.Start(
            new ChunkedStream(body, Harness.ChunkSizes(body.Length, "whole")),
            new StreamResponseInfo { StatusCode = 200, ContentType = Harness.EventStreamContentType },
            Harness.Options());

        var events = new List<StreamEvent>();
        await foreach (var streamEvent in stream)
        {
            events.Add(streamEvent);
        }

        stream.Cancel();
        stream.Cancel();

        Assert.Single(events, e => e.IsTerminal);
        Assert.Equal(FinishReason.Stop, events[^1].FinishReason);
    }
}
