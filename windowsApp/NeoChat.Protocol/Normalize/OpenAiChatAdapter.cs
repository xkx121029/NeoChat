using System.Text.Json;
using NeoChat.Protocol.Diagnostics;
using NeoChat.Protocol.Errors;
using NeoChat.Protocol.Events;
using NeoChat.Protocol.Pipeline;
using NeoChat.Protocol.Transport;

namespace NeoChat.Protocol.Normalize;

/// <summary>
/// OpenAI Chat Completions mapping (spec/stream-events.md §4.1), shared by <c>openai_chat</c>,
/// <c>openai_compatible</c> and <c>openrouter</c>.
/// </summary>
public sealed class OpenAiChatAdapter : IChatStreamAdapter
{
    private readonly StreamEventEmitter _emitter;
    private readonly StreamTrace _trace;
    private readonly ErrorContext _context;
    private readonly PrefixDeduper _text;
    private readonly PrefixDeduper _reasoning;
    private FinishReason? _pendingFinishReason;
    private string? _pendingRawFinishReason;
    private bool _multiChoiceNoticed;

    public OpenAiChatAdapter(ChatStreamOptions options, StreamEventEmitter emitter, StreamTrace trace, ErrorContext context)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(emitter);
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(context);
        _emitter = emitter;
        _trace = trace;
        _context = context;
        _text = new PrefixDeduper(options.DeduplicatesText, trace);
        _reasoning = new PrefixDeduper(options.DeduplicatesText, trace);
    }

    /// <summary>Total characters of <c>delta</c> text emitted, used by <c>PARSE_INCOMPLETE.partial_text_length</c>.</summary>
    public int EmittedTextLength => _text.EmittedLength;

    public void OnFrame(SseFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (_emitter.IsTerminated)
        {
            return;
        }

        var payload = frame.Data;
        if (IsDoneMarker(payload))
        {
            _emitter.Done(_pendingFinishReason ?? FinishReason.Stop, _pendingRawFinishReason);
            return;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            if (frame.Partial)
            {
                _trace.Note("dropped an unterminated trailing frame");
                return;
            }

            _emitter.Failed(ErrorMapper.ParseInvalid(_context, payload));
            return;
        }

        using (document)
        {
            HandleRoot(document.RootElement, payload);
        }
    }

    public void OnEof()
    {
        if (_emitter.IsTerminated)
        {
            return;
        }

        if (_pendingFinishReason is { } reason)
        {
            _emitter.Done(reason, _pendingRawFinishReason);
            return;
        }

        _emitter.Failed(ErrorMapper.Incomplete(_context, _text.EmittedLength));
    }

    private static bool IsDoneMarker(string payload) =>
        payload.AsSpan().Trim().SequenceEqual("[DONE]");

    private void HandleRoot(JsonElement root, string payload)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            _emitter.Failed(ErrorMapper.ParseInvalid(_context, payload, providerCode: $"unexpected_{root.ValueKind}"));
            return;
        }

        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            _emitter.Failed(ErrorMapper.FromProviderErrorBody(payload, error, httpStatus: null, _context));
            return;
        }

        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            _emitter.Usage(ReadUsage(usage));
        }

        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            return;
        }

        if (choices.GetArrayLength() > 1)
        {
            _trace.MultiChoiceDropped += choices.GetArrayLength() - 1;
            if (!_multiChoiceNoticed)
            {
                _multiChoiceNoticed = true;
                _emitter.Notice(NoticeInfo.Of(NoticeCode.MultiChoiceIgnored));
            }
        }

        HandleChoice(SelectPrimaryChoice(choices));
    }

    private static JsonElement SelectPrimaryChoice(JsonElement choices)
    {
        JsonElement first = default;
        var seen = false;
        foreach (var choice in choices.EnumerateArray())
        {
            if (choice.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!seen)
            {
                first = choice;
                seen = true;
            }

            if (choice.TryGetProperty("index", out var index) && index.ValueKind == JsonValueKind.Number && index.GetInt32() == 0)
            {
                return choice;
            }
        }

        return first;
    }

    private void HandleChoice(JsonElement choice)
    {
        if (choice.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (choice.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object)
        {
            HandleDelta(delta);
        }

        if (choice.TryGetProperty("finish_reason", out var finishReason)
            && finishReason.ValueKind == JsonValueKind.String
            && finishReason.GetString() is { } raw)
        {
            var mapped = MapFinishReason(raw);
            if (mapped is { } reason)
            {
                _pendingFinishReason = reason;
                _pendingRawFinishReason = raw;
            }
            else
            {
                _trace.Note($"unmapped finish_reason '{raw}'");
            }
        }
    }

    /// <summary>Fixed order inside one frame: <c>delta</c>, then <c>reasoning_delta</c>, then tool calls by index (spec §4.1).</summary>
    private void HandleDelta(JsonElement delta)
    {
        if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
        {
            EmitText(content.GetString());
        }

        string? reasoning = null;
        if (delta.TryGetProperty("reasoning_content", out var reasoningContent) && reasoningContent.ValueKind == JsonValueKind.String)
        {
            reasoning = reasoningContent.GetString();
        }

        if (delta.TryGetProperty("reasoning", out var reasoningAlias) && reasoningAlias.ValueKind == JsonValueKind.String)
        {
            if (reasoning is not null)
            {
                _trace.ReasoningFieldConflicts++;
                _trace.Note("reasoning_field_conflict");
            }
            else
            {
                reasoning = reasoningAlias.GetString();
            }
        }

        if (reasoning is not null)
        {
            EmitReasoning(reasoning);
        }

        if (delta.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
        {
            var ordered = new List<JsonElement>(toolCalls.GetArrayLength());
            foreach (var item in toolCalls.EnumerateArray())
            {
                ordered.Add(item);
            }

            ordered.Sort((left, right) => ReadToolIndex(left).CompareTo(ReadToolIndex(right)));
            foreach (var item in ordered)
            {
                HandleToolCall(item);
            }
        }
    }

    private void HandleToolCall(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var index = ReadToolIndex(item);
        string? id = null;
        string? name = null;
        string? args = null;

        if (item.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String)
        {
            id = idElement.GetString();
        }

        if (item.TryGetProperty("function", out var function) && function.ValueKind == JsonValueKind.Object)
        {
            if (function.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
            {
                name = nameElement.GetString();
            }

            if (function.TryGetProperty("arguments", out var argsElement) && argsElement.ValueKind == JsonValueKind.String)
            {
                args = argsElement.GetString();
            }
        }
        else if (item.TryGetProperty("arguments", out var flatArgs) && flatArgs.ValueKind == JsonValueKind.String)
        {
            args = flatArgs.GetString();
        }

        _emitter.ToolCallDelta(index, id, name, args);
    }

    private static int ReadToolIndex(JsonElement item) =>
        item.TryGetProperty("index", out var index) && index.ValueKind == JsonValueKind.Number && index.TryGetInt32(out var value) ? value : 0;

    private void EmitText(string? value)
    {
        if (_text.Accept(value) is { Length: > 0 } increment)
        {
            _emitter.Delta(increment);
        }
    }

    private void EmitReasoning(string? value)
    {
        if (_reasoning.Accept(value) is { Length: > 0 } increment)
        {
            _emitter.ReasoningDelta(increment);
        }
    }

    private static FinishReason? MapFinishReason(string raw) => raw switch
    {
        "stop" => FinishReason.Stop,
        "length" => FinishReason.Length,
        "tool_calls" or "function_call" => FinishReason.ToolCalls,
        "content_filter" => FinishReason.ContentFilter,
        _ => null,
    };

    private static UsageInfo ReadUsage(JsonElement usage)
    {
        var input = ReadLong(usage, "prompt_tokens") ?? 0;
        var output = ReadLong(usage, "completion_tokens") ?? 0;
        long? cached = null;
        if (usage.TryGetProperty("prompt_tokens_details", out var promptDetails) && promptDetails.ValueKind == JsonValueKind.Object)
        {
            cached = ReadLong(promptDetails, "cached_tokens");
        }

        long? reasoning = null;
        if (usage.TryGetProperty("completion_tokens_details", out var completionDetails) && completionDetails.ValueKind == JsonValueKind.Object)
        {
            reasoning = ReadLong(completionDetails, "reasoning_tokens");
        }

        ProviderCost? cost = null;
        if (usage.TryGetProperty("cost", out var costElement) && costElement.ValueKind == JsonValueKind.Number)
        {
            cost = new ProviderCost(costElement.GetRawText(), "USD");
        }

        return new UsageInfo(input, output)
        {
            Cached = cached,
            Reasoning = reasoning,
            Total = ReadLong(usage, "total_tokens"),
            Estimated = false,
            ProviderCost = cost,
        };
    }

    private static long? ReadLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;

    /// <summary>
    /// Prefix de-duplication for cumulative endpoints (I11) and for conformance samples flagged
    /// <c>duplicate=true</c>. Never applied to plain incremental endpoints: identical consecutive
    /// tokens there are real content.
    /// </summary>
    private sealed class PrefixDeduper
    {
        private readonly bool _enabled;
        private readonly StreamTrace _trace;
        private string _full = string.Empty;

        public PrefixDeduper(bool enabled, StreamTrace trace)
        {
            _enabled = enabled;
            _trace = trace;
        }

        public int EmittedLength { get; private set; }

        public string? Accept(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            if (!_enabled)
            {
                EmittedLength += value.Length;
                return value;
            }

            if (_full.Length == 0)
            {
                _full = value;
                EmittedLength += value.Length;
                return value;
            }

            if (value.Length > _full.Length && value.StartsWith(_full, StringComparison.Ordinal))
            {
                var increment = value[_full.Length..];
                _full = value;
                EmittedLength += increment.Length;
                return increment;
            }

            if (string.Equals(value, _full, StringComparison.Ordinal))
            {
                _trace.DuplicateFramesSkipped++;
                return null;
            }

            _trace.RewritesIgnored++;
            _trace.Note("cumulative rewrite does not extend the emitted prefix; frame dropped");
            return null;
        }
    }
}
