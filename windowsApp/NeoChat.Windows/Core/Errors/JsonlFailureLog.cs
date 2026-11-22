using System.Globalization;
using System.Text;
using NeoChat.Protocol.Events;

namespace NeoChat.Windows.Core.Errors;

/// <summary>
/// Appends one JSON object per failure to <c>failures.jsonl</c>. The inner object is the protocol
/// layer's own <c>failed</c> event line, so the persisted record stays byte-comparable with the wire
/// format (spec/stream-events.md §3.2) instead of a second, drifting schema.
/// </summary>
public sealed class JsonlFailureLog : IFailureLog
{
    private readonly TimeProvider _time;

    public JsonlFailureLog(string directory, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        Location = Path.Combine(directory, "failures.jsonl");
        _time = time ?? TimeProvider.System;
    }

    public string Location { get; }

    /// <summary>
    /// Write failures are swallowed on purpose: a full disk must not turn a provider error into a
    /// crash. The last write error is kept for diagnostics.
    /// </summary>
    public Exception? LastWriteError { get; private set; }

    public void Record(ProviderError providerError)
    {
        ArgumentNullException.ThrowIfNull(providerError);
        var failed = new StreamEvent { Kind = StreamEventKind.Failed, Seq = 0, Error = providerError };
        var line = new StringBuilder(256)
            .Append("{\"ts\":\"")
            .Append(_time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture))
            .Append("\",\"failed\":")
            .Append(failed.ToJsonLine())
            .Append("}\n")
            .ToString();

        try
        {
            File.AppendAllText(Location, line, Encoding.UTF8);
            LastWriteError = null;
        }
        catch (IOException ex)
        {
            LastWriteError = ex;
        }
        catch (UnauthorizedAccessException ex)
        {
            LastWriteError = ex;
        }
    }
}
