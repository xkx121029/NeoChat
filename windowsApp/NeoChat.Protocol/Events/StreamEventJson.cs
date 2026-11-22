using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace NeoChat.Protocol.Events;

/// <summary>
/// Canonical JSON Lines writer for <see cref="StreamEvent"/>: keys sorted ordinal, no whitespace,
/// one event per line (spec/conformance/CASES.md §0). Two runs over the same bytes must produce
/// byte-identical text, so every key is written explicitly in sorted order.
/// </summary>
public static class StreamEventJson
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string ToLine(StreamEvent streamEvent)
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            WriteEvent(writer, streamEvent);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static string ToJsonl(IReadOnlyList<StreamEvent> events)
    {
        var builder = new StringBuilder(events.Count * 64);
        foreach (var streamEvent in events)
        {
            builder.Append(ToLine(streamEvent)).Append('\n');
        }

        return builder.ToString();
    }

    public static string Sha256Hex(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(hash);
    }

    private static void WriteEvent(Utf8JsonWriter writer, StreamEvent streamEvent)
    {
        writer.WriteStartObject();
        switch (streamEvent.Kind)
        {
            case StreamEventKind.Delta:
                WriteType(writer, streamEvent, "delta");
                WriteOptionalString(writer, "text", streamEvent.Text);
                break;

            case StreamEventKind.ReasoningDelta:
                WriteType(writer, streamEvent, "reasoning_delta");
                WriteOptionalString(writer, "text", streamEvent.Text);
                break;

            case StreamEventKind.ToolCallDelta:
                if (streamEvent.Args is not null)
                {
                    writer.WriteString("args", streamEvent.Args);
                }

                WriteOptionalString(writer, "id", streamEvent.ToolCallId);
                if (streamEvent.Index is { } index)
                {
                    writer.WriteNumber("index", index);
                }

                WriteOptionalString(writer, "name", streamEvent.ToolName);
                WriteType(writer, streamEvent, "tool_call_delta");
                break;

            case StreamEventKind.ToolCallEnd:
                if (streamEvent.Index is { } endedIndex)
                {
                    writer.WriteNumber("index", endedIndex);
                }

                WriteType(writer, streamEvent, "tool_call_end");
                break;

            case StreamEventKind.Usage:
                WriteUsage(writer, streamEvent);
                break;

            case StreamEventKind.Notice:
                WriteNotice(writer, streamEvent);
                break;

            case StreamEventKind.Done:
                if (streamEvent.FinishReason is { } finishReason)
                {
                    writer.WriteString("finish_reason", FinishReasonWire.ToWire(finishReason));
                }

                WriteOptionalString(writer, "raw_finish_reason", streamEvent.RawFinishReason);
                WriteType(writer, streamEvent, "done");
                break;

            case StreamEventKind.Failed:
                if (streamEvent.Error is { } error)
                {
                    writer.WritePropertyName("error");
                    WriteError(writer, error);
                }

                WriteType(writer, streamEvent, "failed");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(streamEvent), streamEvent.Kind, "unknown event kind");
        }

        writer.WriteEndObject();
    }

    private static void WriteType(Utf8JsonWriter writer, StreamEvent streamEvent, string type)
    {
        writer.WriteNumber("seq", streamEvent.Seq);
        writer.WriteString("t", type);
    }

    private static void WriteUsage(Utf8JsonWriter writer, StreamEvent streamEvent)
    {
        var usage = streamEvent.Usage ?? throw new ArgumentException("usage event without usage payload", nameof(streamEvent));
        if (usage.ErrorBoundPct is { } errorBoundPct)
        {
            writer.WriteNumber("error_bound_pct", errorBoundPct);
        }

        if (usage.Estimated is { } estimated)
        {
            writer.WriteBoolean("estimated", estimated);
        }

        writer.WriteNumber("input", usage.Input);
        writer.WriteNumber("output", usage.Output);
        if (usage.ProviderCost is { } cost)
        {
            writer.WritePropertyName("provider_cost");
            writer.WriteStartObject();
            writer.WriteString("amount", cost.Amount);
            writer.WriteString("currency", cost.Currency);
            writer.WriteEndObject();
        }

        if (usage.Reasoning is { } reasoning)
        {
            writer.WriteNumber("reasoning", reasoning);
        }

        WriteType(writer, streamEvent, "usage");
        if (usage.Total is { } total)
        {
            writer.WriteNumber("total", total);
        }
    }

    private static void WriteNotice(Utf8JsonWriter writer, StreamEvent streamEvent)
    {
        var notice = streamEvent.Notice ?? throw new ArgumentException("notice event without notice payload", nameof(streamEvent));
        writer.WriteString("code", NoticeCodeWire.ToWire(notice.Code));
        if (notice.Parameters is { Count: > 0 } parameters)
        {
            writer.WritePropertyName("params");
            writer.WriteStartObject();
            foreach (var (key, values) in parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(key);
                if (values.Count == 1)
                {
                    writer.WriteStringValue(values[0]);
                }
                else
                {
                    writer.WriteStartArray();
                    foreach (var value in values)
                    {
                        writer.WriteStringValue(value);
                    }

                    writer.WriteEndArray();
                }
            }

            writer.WriteEndObject();
        }

        WriteType(writer, streamEvent, "notice");
    }

    /// <summary>
    /// The canonical error object (spec/errors.md §2) as one JSON string. Persistence stores this in
    /// <c>message.error_json</c>, so a reloaded failure holds exactly the object the stream carried.
    /// </summary>
    public static string ErrorToJson(ProviderError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var buffer = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            WriteError(writer, error);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteError(Utf8JsonWriter writer, ProviderError error)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("actions");
        writer.WriteStartArray();
        foreach (var action in error.Actions)
        {
            WriteAction(writer, action);
        }

        writer.WriteEndArray();

        writer.WritePropertyName("causes");
        writer.WriteStartArray();
        foreach (var cause in error.Causes)
        {
            writer.WriteStringValue(cause);
        }

        writer.WriteEndArray();

        writer.WriteString("code", error.Code);
        if (error.HttpStatus is { } httpStatus)
        {
            writer.WriteNumber("http_status", httpStatus);
        }

        WriteOptionalString(writer, "model", error.Model);

        writer.WritePropertyName("params");
        writer.WriteStartObject();
        foreach (var (key, value) in error.Params.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.WriteString(key, value);
        }

        writer.WriteEndObject();

        WriteOptionalString(writer, "provider", error.Provider);
        WriteOptionalString(writer, "provider_code", error.ProviderCode);
        WriteOptionalString(writer, "raw", error.Raw);
        WriteOptionalString(writer, "request_id", error.RequestId);
        if (error.RetryAfterMs is { } retryAfterMs)
        {
            writer.WriteNumber("retry_after_ms", retryAfterMs);
        }

        writer.WriteBoolean("retryable", error.Retryable);
        writer.WriteString("stage", error.Stage);
        writer.WriteString("title_key", error.TitleKey);
        writer.WriteString("trace_id", error.TraceId);

        writer.WriteEndObject();
    }

    private static void WriteAction(Utf8JsonWriter writer, ErrorAction action)
    {
        writer.WriteStartObject();
        writer.WriteString("a", action.A);
        WriteOptionalString(writer, "focus", action.Focus);
        if (action.Index is { } index)
        {
            writer.WriteNumber("index", index);
        }

        if (action.Ms is { } ms)
        {
            writer.WriteNumber("ms", ms);
        }

        WriteOptionalString(writer, "model_id", action.ModelId);
        if (action.Require is { Count: > 0 } require)
        {
            writer.WritePropertyName("require");
            writer.WriteStartArray();
            foreach (var item in require)
            {
                writer.WriteStringValue(item);
            }

            writer.WriteEndArray();
        }

        WriteOptionalString(writer, "topic", action.Topic);
        writer.WriteEndObject();
    }

    private static void WriteOptionalString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }
}
