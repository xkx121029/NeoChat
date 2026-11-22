using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NeoChat.Protocol.Pipeline;

namespace NeoChat.Protocol.Tests.Support;

/// <summary>Serves one sample in the chunk sizes the runner would use, and records whether it was closed.</summary>
internal sealed class ChunkedStream : Stream
{
    private readonly byte[] _data;
    private readonly IReadOnlyList<int> _sizes;
    private int _offset;
    private int _chunkIndex;
    private int _remainingInChunk;

    public ChunkedStream(byte[] data, IReadOnlyList<int> sizes)
    {
        _data = data;
        _sizes = sizes;
    }

    public bool Disposed { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => _data.Length;

    public override long Position
    {
        get => _offset;
        set => throw new NotSupportedException();
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);

        if (_offset >= _data.Length || buffer.IsEmpty)
        {
            return 0;
        }

        if (_remainingInChunk == 0)
        {
            _remainingInChunk = _sizes[_chunkIndex++];
        }

        var take = Math.Min(Math.Min(_remainingInChunk, buffer.Length), _data.Length - _offset);
        _data.AsSpan(_offset, take).CopyTo(buffer);
        _offset += take;
        _remainingInChunk -= take;
        return take;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(buffer.Span));
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}

/// <summary>
/// Minimal HTTP/1.1 server over a raw socket, so tests can assert on connection lifetime rather
/// than on a mocked stream: RST, idle silence, and a connection held open after <c>[DONE]</c>.
/// </summary>
internal sealed class RawHttpServer : IAsyncDisposable
{
    public const string SseHead = "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream; charset=utf-8\r\nCache-Control: no-cache\r\nTransfer-Encoding: chunked\r\n\r\n";

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    private RawHttpServer(Func<RawHttpServer, NetworkStream, Socket, CancellationToken, Task> handler)
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = Task.Run(() => AcceptLoopAsync(handler));
    }

    public int Port { get; }

    public string Url => $"http://127.0.0.1:{Port}/v1/chat/completions";

    /// <summary>Completes when a write to the client fails, i.e. the client really closed the socket.</summary>
    public TaskCompletionSource ClientClosed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Connected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string? RequestText { get; private set; }

    public static RawHttpServer Start(Func<RawHttpServer, NetworkStream, Socket, CancellationToken, Task> handler) => new(handler);

    public async Task WriteHeadAsync(NetworkStream stream, CancellationToken cancellationToken) =>
        await RawWriteAsync(stream, Encoding.ASCII.GetBytes(SseHead), cancellationToken);

    /// <summary>Writes one chunked HTTP body segment, the framing a real SSE response uses.</summary>
    public async Task WriteAsync(NetworkStream stream, string text, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var prefix = Encoding.ASCII.GetBytes(payload.Length.ToString("x", System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
        await RawWriteAsync(stream, prefix, cancellationToken);
        await RawWriteAsync(stream, payload, cancellationToken);
        await RawWriteAsync(stream, "\r\n"u8.ToArray(), cancellationToken);
    }

    private async Task RawWriteAsync(NetworkStream stream, byte[] bytes, CancellationToken cancellationToken)
    {
        try
        {
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or SocketException)
        {
            ClientClosed.TrySetResult();
            throw;
        }
    }

    public static async Task<(HttpClient Client, ChatStreamSession Stream, Stream Body)> ConnectAsync(
        string url,
        ChatStreamOptions options,
        ActiveStreamCounter counter,
        int timeoutMs = 5000)
    {
        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.None,
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent("{\"model\":\"gpt-test\",\"stream\":true}", Encoding.UTF8, "application/json"),
        };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var body = await response.Content.ReadAsStreamAsync();
        var stream = ChatStreamSession.Start(
            body,
            new StreamResponseInfo { StatusCode = (int)response.StatusCode, ContentType = response.Content.Headers.ContentType?.ToString(), Headers = ReadHeaders(response) },
            options,
            counter);
        return (client, stream, body);
    }

    public static IReadOnlyDictionary<string, string> ReadHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, values) in response.Headers)
        {
            headers[key] = string.Join(",", values);
        }

        foreach (var (key, values) in response.Content.Headers)
        {
            headers[key] = string.Join(",", values);
        }

        return headers;
    }

    public static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs, string because)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(5).ConfigureAwait(false);
        }

        Assert.Fail($"waited {timeoutMs} ms for {because}");
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // The server task ends by cancellation; nothing to report from a test teardown.
        }

        _listener.Stop();
        _cts.Dispose();
    }

    private async Task AcceptLoopAsync(Func<RawHttpServer, NetworkStream, Socket, CancellationToken, Task> handler)
    {
        try
        {
            using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
            using var stream = client.GetStream();
            RequestText = await ReadRequestHeadAsync(stream, _cts.Token);
            Connected.TrySetResult();
            await handler(this, stream, client.Client, _cts.Token);
            await Task.Delay(50, _cts.Token);
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // Cancelled or the client vanished mid-write: both are ordinary ends for a test server.
        }
    }

    private static async Task<string> ReadRequestHeadAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var text = new StringBuilder();
        while (!text.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            text.Append(Encoding.UTF8.GetString(buffer, 0, read));
        }

        return text.ToString();
    }
}
