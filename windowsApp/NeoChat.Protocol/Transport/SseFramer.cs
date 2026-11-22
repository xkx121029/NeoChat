using System.Text;
using NeoChat.Protocol.Diagnostics;

namespace NeoChat.Protocol.Transport;

/// <summary>One dispatched SSE frame. <paramref name="Partial"/> marks a frame recovered from an unterminated final line at EOF.</summary>
public sealed record SseFrame(string Data, string? EventName = null, string? Id = null, int? Retry = null, bool Partial = false);

public sealed class SseFrameTooLargeException : Exception
{
    public SseFrameTooLargeException(long limitBytes)
        : base($"SSE payload exceeded the {limitBytes} byte limit")
    {
        LimitBytes = limitBytes;
    }

    public long LimitBytes { get; }
}

/// <summary>
/// L1 framing (spec/stream-events.md §2.1): stateful UTF-8 decoding across chunk boundaries,
/// <c>\n</c> / <c>\r\n</c> / <c>\r</c> terminators, multi-line <c>data:</c>, comment heartbeats,
/// leading BOM, U+FFFD substitution for illegal bytes, and the 16 MiB payload ceiling.
/// </summary>
public sealed class SseFramer
{
    public const int DefaultMaxPayloadBytes = 16 * 1024 * 1024;

    private readonly Decoder _decoder;
    private readonly InvalidUtf8CountingFallback _fallback;
    private readonly char[] _chars = new char[8192];
    private readonly StringBuilder _line = new();
    private readonly StringBuilder _data = new();
    private readonly long _maxPayloadBytes;
    private string? _eventName;
    private string? _id;
    private int? _retry;
    private bool _pendingCr;
    private bool _atStreamStart = true;
    private long _bytesConsumed;
    private long _lineStartBytes;
    private long _frameStartBytes;

    public SseFramer(StreamTrace trace, int maxPayloadBytes = DefaultMaxPayloadBytes)
    {
        ArgumentNullException.ThrowIfNull(trace);
        if (maxPayloadBytes < 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes), maxPayloadBytes, "the frame ceiling may not go below 1 MiB");
        }

        Trace = trace;
        _maxPayloadBytes = maxPayloadBytes;
        _fallback = new InvalidUtf8CountingFallback(trace);
        var encoding = Encoding.GetEncoding(
            "utf-8",
            EncoderFallback.ReplacementFallback,
            _fallback);
        _decoder = encoding.GetDecoder();
    }

    public StreamTrace Trace { get; }

    /// <summary>Bytes handed to the framer so far.</summary>
    public long BytesConsumed => _bytesConsumed;

    /// <summary>Feeds one transferred chunk. Appends every frame completed inside it to <paramref name="output"/>.</summary>
    public void Feed(ReadOnlySpan<byte> chunk, ICollection<SseFrame> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        while (!chunk.IsEmpty)
        {
            _decoder.Convert(chunk, _chars, flush: false, out var bytesUsed, out var charsUsed, out _);
            _bytesConsumed += bytesUsed;
            chunk = chunk[bytesUsed..];
            if (charsUsed > 0)
            {
                ProcessChars(_chars.AsSpan(0, charsUsed), output);
            }

            CheckLimits();

            if (bytesUsed == 0 && charsUsed == 0)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Signals EOF: flushes the decoder (which turns trailing incomplete sequences into U+FFFD),
    /// recovers an unterminated final line, and dispatches the frame still open.
    /// </summary>
    public void Complete(ICollection<SseFrame> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _decoder.Convert(ReadOnlySpan<byte>.Empty, _chars, flush: true, out _, out var charsUsed, out _);
        if (charsUsed > 0)
        {
            ProcessChars(_chars.AsSpan(0, charsUsed), output);
        }

        CheckLimits();

        if (_pendingCr)
        {
            _pendingCr = false;
            EndLine(output);
        }

        var partialTail = _line.Length > 0;
        if (partialTail)
        {
            var text = _line.ToString();
            _line.Clear();
            HandleLine(text.AsSpan(), output);
        }

        DispatchFrame(output, partialTail);
    }

    private void ProcessChars(ReadOnlySpan<char> chars, ICollection<SseFrame> output)
    {
        foreach (var ch in chars)
        {
            if (_atStreamStart)
            {
                if (ch == '\uFEFF')
                {
                    continue;
                }

                _atStreamStart = false;
            }

            if (_pendingCr)
            {
                _pendingCr = false;
                if (ch == '\n')
                {
                    continue;
                }
            }

            switch (ch)
            {
                case '\r':
                    _pendingCr = true;
                    EndLine(output);
                    break;
                case '\n':
                    EndLine(output);
                    break;
                default:
                    _line.Append(ch);
                    break;
            }
        }
    }

    private void EndLine(ICollection<SseFrame> output)
    {
        var text = _line.ToString();
        if (_line.Length > 0)
        {
            _line.Clear();
        }

        _lineStartBytes = _bytesConsumed;
        HandleLine(text.AsSpan(), output);
    }

    private void HandleLine(ReadOnlySpan<char> line, ICollection<SseFrame> output)
    {
        if (line.IsEmpty)
        {
            DispatchFrame(output, partial: false);
            return;
        }

        if (line[0] == ':')
        {
            Trace.HeartbeatLines++;
            return;
        }

        var colon = line.IndexOf(':');
        ReadOnlySpan<char> field;
        ReadOnlySpan<char> value;
        if (colon < 0)
        {
            field = line;
            value = default;
        }
        else
        {
            field = line[..colon];
            value = line[(colon + 1)..];
            if (!value.IsEmpty && value[0] == ' ')
            {
                value = value[1..];
            }
        }

        if (field.SequenceEqual("data"))
        {
            if (_data.Length > 0)
            {
                _data.Append('\n');
            }

            _data.Append(value);
        }
        else if (field.SequenceEqual("event"))
        {
            _eventName = value.ToString();
        }
        else if (field.SequenceEqual("id"))
        {
            _id = value.ToString();
        }
        else if (field.SequenceEqual("retry"))
        {
            if (int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var retry))
            {
                _retry = retry;
            }
        }
    }

    private void DispatchFrame(ICollection<SseFrame> output, bool partial)
    {
        if (_data.Length > 0)
        {
            output.Add(new SseFrame(_data.ToString(), _eventName, _id, _retry, partial));
        }

        _data.Clear();
        _eventName = null;
        _id = null;
        _retry = null;
        _frameStartBytes = _bytesConsumed;
    }

    private void CheckLimits()
    {
        var lineBytes = _bytesConsumed - _lineStartBytes;
        var frameBytes = _bytesConsumed - _frameStartBytes;
        var size = Math.Max(lineBytes, frameBytes);
        if (size > _maxPayloadBytes)
        {
            throw new SseFrameTooLargeException(_maxPayloadBytes);
        }
    }

    private sealed class InvalidUtf8CountingFallback : DecoderFallback
    {
        private readonly StreamTrace _trace;

        public InvalidUtf8CountingFallback(StreamTrace trace) => _trace = trace;

        public override int MaxCharCount => 1;

        public override DecoderFallbackBuffer CreateFallbackBuffer() => new Buffer(this);

        private sealed class Buffer : DecoderFallbackBuffer
        {
            private readonly InvalidUtf8CountingFallback _owner;
            private bool _pending;

            public Buffer(InvalidUtf8CountingFallback owner) => _owner = owner;

            public override int Remaining => _pending ? 1 : 0;

            public override bool Fallback(byte[] bytesUnknown, int index)
            {
                _owner._trace.InvalidUtf8Bytes += bytesUnknown.Length;
                _pending = true;
                return true;
            }

            public override char GetNextChar()
            {
                if (!_pending)
                {
                    return '\0';
                }

                _pending = false;
                return '\uFFFD';
            }

            public override bool MovePrevious()
            {
                if (_pending)
                {
                    return false;
                }

                _pending = true;
                return true;
            }
        }
    }
}
