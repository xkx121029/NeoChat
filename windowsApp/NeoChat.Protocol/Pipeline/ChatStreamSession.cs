using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NeoChat.Protocol.Diagnostics;
using NeoChat.Protocol.Errors;
using NeoChat.Protocol.Events;
using NeoChat.Protocol.Normalize;
using NeoChat.Protocol.Transport;

namespace NeoChat.Protocol.Pipeline;

/// <summary>The HTTP response envelope a stream starts from (spec/stream-events.md §2.3).</summary>
public sealed record StreamResponseInfo
{
    public required int StatusCode { get; init; }

    public string? ContentType { get; init; }

    public IReadOnlyDictionary<string, string>? Headers { get; init; }
}

/// <summary>
/// Counts live streams. Conformance <c>socket</c> asserts read this hook; a pooled idle connection
/// is not active, so the count falls to zero the moment the terminal event is delivered.
/// </summary>
public sealed class ActiveStreamCounter
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    internal void Increment() => Interlocked.Increment(ref _count);

    internal void Decrement() => Interlocked.Decrement(ref _count);
}

public sealed class ReadIdleTimeoutException : Exception
{
    public ReadIdleTimeoutException(long idleMs)
        : base($"no bytes arrived for {idleMs} ms")
    {
        IdleMs = idleMs;
    }

    public long IdleMs { get; }
}

/// <summary>
/// One stream attempt: response envelope in, <see cref="StreamEvent"/> sequence out.
/// Owns the body stream, so terminating the event sequence releases the connection (I12), and
/// <see cref="Cancel"/> closes it rather than merely stopping the read (I10).
/// </summary>
public sealed class ChatStreamSession : IAsyncEnumerable<StreamEvent>, IDisposable
{
    private const int MaxErrorBodyBytes = 64 * 1024;
    private const int MaxJsonBodyBytes = 1024 * 1024;

    private readonly Stream _body;
    private readonly StreamResponseInfo _response;
    private readonly ChatStreamOptions _options;
    private readonly StreamTrace _trace;
    private readonly ErrorContext _context;
    private readonly StreamEventEmitter _emitter;
    private readonly IChatStreamAdapter _adapter;
    private readonly ActiveStreamCounter? _counter;
    private readonly CancellationTokenSource _cancelSource = new();
    private int _cancelRequested;
    private int _started;
    private int _bodyReleased;

    private ChatStreamSession(Stream body, StreamResponseInfo response, ChatStreamOptions options, ActiveStreamCounter? counter)
    {
        _body = body;
        _response = response;
        _options = options;
        _counter = counter;
        _trace = new StreamTrace();
        _context = new ErrorContext { Provider = options.Provider, Model = options.Model };
        _emitter = new StreamEventEmitter(_trace);
        _adapter = ChatStreamAdapterFactory.Create(options.Protocol, options, _emitter, _trace, _context);
    }

    public StreamTrace Trace => _trace;

    public string TraceId => _context.TraceId;

    public static ChatStreamSession Start(Stream body, StreamResponseInfo response, ChatStreamOptions options, ActiveStreamCounter? counter = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(options);
        return new ChatStreamSession(body, response, options, counter);
    }

    /// <summary>
    /// User cancel: closes the underlying connection immediately and lets the sequence end with
    /// <c>done{aborted_user}</c> (spec §5.1, I10). Safe to call from another thread, and idempotent.
    /// </summary>
    public void Cancel()
    {
        if (Interlocked.Exchange(ref _cancelRequested, 1) == 1)
        {
            return;
        }

        try
        {
            _cancelSource.Cancel();
        }
        catch (AggregateException)
        {
            // A throwing cancellation callback must not stop the socket from being closed.
        }

        ReleaseBody();
    }

    public IAsyncEnumerator<StreamEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            throw new InvalidOperationException("a ChatStreamSession may only be enumerated once");
        }

        return EnumerateAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
    }

    public void Dispose()
    {
        ReleaseBody();
        _cancelSource.Dispose();
    }

    private async IAsyncEnumerable<StreamEvent> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _counter?.Increment();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancelSource.Token);
        var inner = ReadEventsAsync(linked.Token).GetAsyncEnumerator(linked.Token);
        try
        {
            while (true)
            {
                var step = await MoveAsync(inner).ConfigureAwait(false);
                if (step.Error is { } error)
                {
                    _trace.Note($"stream ended on {error.GetType().Name}");
                    foreach (var terminal in TerminalEvents(error))
                    {
                        yield return terminal;
                    }

                    yield break;
                }

                if (!step.Moved)
                {
                    foreach (var terminal in EndOfBodyEvents())
                    {
                        yield return terminal;
                    }

                    yield break;
                }

                yield return step.Current!;
                if (step.Current!.IsTerminal)
                {
                    yield break;
                }
            }
        }
        finally
        {
            await inner.DisposeAsync().ConfigureAwait(false);
            ReleaseBody();
            _counter?.Decrement();
        }
    }

    private async IAsyncEnumerable<StreamEvent> ReadEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_response.StatusCode is < 200 or >= 300)
        {
            var errorBody = await ReadCappedAsync(MaxErrorBodyBytes, cancellationToken).ConfigureAwait(false);
            _emitter.Failed(ErrorMapper.FromHttpStatus(_response.StatusCode, errorBody, _response.Headers, _context));
            foreach (var terminal in _emitter.Drain())
            {
                yield return terminal;
            }

            yield break;
        }

        if (LooksLikeJson(_response.ContentType))
        {
            var jsonBody = await ReadCappedAsync(MaxJsonBodyBytes, cancellationToken).ConfigureAwait(false);
            EmitJsonBodyFailure(jsonBody);
            foreach (var terminal in _emitter.Drain())
            {
                yield return terminal;
            }

            yield break;
        }

        var framer = new SseFramer(_trace, _options.MaxPayloadBytes);
        var frames = new List<SseFrame>(16);
        var buffer = new byte[8192];

        while (!_emitter.IsTerminated)
        {
            var read = await ReadWithIdleTimeoutAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            frames.Clear();
            framer.Feed(buffer.AsSpan(0, read), frames);
            foreach (var frame in frames)
            {
                if (Volatile.Read(ref _cancelRequested) == 1)
                {
                    yield break;
                }

                _adapter.OnFrame(frame);
                foreach (var emitted in _emitter.Drain())
                {
                    yield return emitted;
                }

                if (_emitter.IsTerminated)
                {
                    break;
                }
            }
        }

        if (_emitter.IsTerminated)
        {
            yield break;
        }

        var tail = new List<SseFrame>(4);
        framer.Complete(tail);
        foreach (var frame in tail)
        {
            _adapter.OnFrame(frame);
            foreach (var emitted in _emitter.Drain())
            {
                yield return emitted;
            }

            if (_emitter.IsTerminated)
            {
                break;
            }
        }
    }

    private IEnumerable<StreamEvent> TerminalEvents(Exception error)
    {
        if (_emitter.IsTerminated)
        {
            return Array.Empty<StreamEvent>();
        }

        if (Volatile.Read(ref _cancelRequested) == 1 || error is OperationCanceledException)
        {
            _emitter.Canceled();
        }
        else if (error is SseFrameTooLargeException)
        {
            _emitter.Failed(ErrorMapper.EventTooLarge(_context));
        }
        else if (error is ReadIdleTimeoutException idleTimeout)
        {
            _emitter.Failed(ErrorMapper.ReadTimeout(_context, idleTimeout.IdleMs));
        }
        else if (error is IOException or ObjectDisposedException or HttpIOException)
        {
            _emitter.Failed(ErrorMapper.ConnectionReset(_context, error.Message));
        }
        else
        {
            _emitter.Failed(ErrorMapper.Internal(_context, error.Message));
        }

        return _emitter.Drain();
    }

    private IReadOnlyList<StreamEvent> EndOfBodyEvents()
    {
        if (_emitter.IsTerminated)
        {
            return Array.Empty<StreamEvent>();
        }

        _adapter.OnEof();
        return _emitter.Drain();
    }

    private static async Task<Step> MoveAsync(IAsyncEnumerator<StreamEvent> inner)
    {
        try
        {
            var moved = await inner.MoveNextAsync().ConfigureAwait(false);
            return new Step(moved, inner.Current, null);
        }
        catch (Exception error)
        {
            return new Step(false, null, error);
        }
    }

    private async Task<string> ReadCappedAsync(int maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var accumulated = new MemoryStream();
        while (accumulated.Length < maxBytes)
        {
            var read = await _body.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, maxBytes - accumulated.Length)), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            accumulated.Write(buffer, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(accumulated.ToArray());
    }

    private async ValueTask<int> ReadWithIdleTimeoutAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.ReadIdleTimeout);
        try
        {
            return await _body.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ReadIdleTimeoutException((long)_options.ReadIdleTimeout.TotalMilliseconds);
        }
    }

    private void EmitJsonBodyFailure(string body)
    {
        var trimmed = body.AsSpan().TrimStart();
        if (trimmed.Length > 0 && trimmed[0] == '{')
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("error", out var error)
                    && error.ValueKind == JsonValueKind.Object)
                {
                    _emitter.Failed(ErrorMapper.FromProviderErrorBody(body, error, _response.StatusCode, _context));
                    return;
                }
            }
            catch (JsonException)
            {
                // Falls through to PARSE_SSE_INVALID with the raw body attached.
            }
        }

        _emitter.Failed(ErrorMapper.ParseInvalid(_context, body));
    }

    private static bool LooksLikeJson(string? contentType) =>
        contentType is not null && contentType.Contains("json", StringComparison.OrdinalIgnoreCase);

    private void ReleaseBody()
    {
        if (Interlocked.Exchange(ref _bodyReleased, 1) == 1)
        {
            return;
        }

        try
        {
            _body.Dispose();
        }
        catch (IOException)
        {
            // Disposing an aborted HTTP body is allowed to fail; the socket is closed either way.
        }
    }

    private readonly record struct Step(bool Moved, StreamEvent? Current, Exception? Error);
}
