using System.Text;
using System.Text.Json.Nodes;
using NeoChat.Protocol.Canonical;
using NeoChat.Protocol.Events;
using NeoChat.Protocol.Json;

namespace NeoChat.Protocol.Request;

/// <summary>Endpoint knobs the builder needs, mirroring the relevant half of <c>ProviderConfig</c>.</summary>
public sealed record OpenAiChatProvider
{
    public required string BaseUrl { get; init; }

    public string? ApiKey { get; init; }

    /// <summary>Provider label stamped on error objects, not sent on the wire.</summary>
    public string ProtocolLabel { get; init; } = "openai_chat";

    /// <summary>Overrides, e.g. <c>x-api-key</c> for a self-hosted gateway (§3.2 last bullet).</summary>
    public IReadOnlyDictionary<string, string>? ExtraHeaders { get; init; }

    public IReadOnlyDictionary<string, string>? ExtraQuery { get; init; }

    /// <summary>Newer models want <c>max_completion_tokens</c>; the capability matrix supplies this.</summary>
    public bool UseMaxCompletionTokens { get; init; }

    public string UserAgent { get; init; } = "NeoChat/1.0 (windows; 10.0)";

    /// <summary>Raw <c>extra_body_json</c>, merged into the top level of the body.</summary>
    public JsonObject? ExtraBody { get; init; }
}

public sealed record PreparedRequest
{
    public required string Method { get; init; }

    public required string Uri { get; init; }

    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    public required byte[] Body { get; init; }

    /// <summary>Notices produced while building (stripped fields). The caller emits them first, before any delta.</summary>
    public IReadOnlyList<NoticeInfo> Notices { get; init; } = Array.Empty<NoticeInfo>();

    public string BodyJson => Encoding.UTF8.GetString(Body);
}

/// <summary>Reads attachment bytes for <c>data_ref</c> parts; the canonical layer carries no base64 (§2 rule 2).</summary>
public interface IAttachmentSource
{
    byte[] Read(string dataRef);
}

/// <summary>OpenAI Chat Completions request construction (spec/providers.md §3.2).</summary>
public static class OpenAiChatRequestBuilder
{
    public static PreparedRequest Build(ChatRequest request, OpenAiChatProvider provider, IAttachmentSource? attachments = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(provider);

        var notices = new List<NoticeInfo>();
        var body = BuildBody(request, provider, attachments, notices);
        var headers = BuildHeaders(request, provider);
        var uri = ResolveUrl(request, provider);
        return new PreparedRequest
        {
            Method = "POST",
            Uri = uri,
            Headers = headers,
            Body = CanonicalJson.SerializeToUtf8(body),
            Notices = notices,
        };
    }

    /// <summary>Path joining rules of spec/providers.md §1.</summary>
    public static string ResolveChatCompletionsUrl(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var parsed) || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("base URL must be an absolute http(s) URL", nameof(baseUrl));
        }

        var trimmed = baseUrl.Trim().TrimEnd('/');
        foreach (var suffix in new[] { "/chat/completions", "/messages", "/responses" })
        {
            if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }
        }

        return trimmed + "/chat/completions";
    }

    private static string ResolveUrl(ChatRequest request, OpenAiChatProvider provider)
    {
        var url = ResolveChatCompletionsUrl(provider.BaseUrl);
        var query = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (request.Extra?.Query is { } extraQuery)
        {
            foreach (var (key, value) in extraQuery)
            {
                query[key] = value;
            }
        }

        if (provider.ExtraQuery is { } providerQuery)
        {
            foreach (var (key, value) in providerQuery)
            {
                query[key] = value;
            }
        }

        if (query.Count == 0)
        {
            return url;
        }

        var builder = new StringBuilder(url);
        var separator = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        foreach (var (key, value) in query)
        {
            builder.Append(separator)
                .Append(Uri.EscapeDataString(key))
                .Append('=')
                .Append(Uri.EscapeDataString(value));
            separator = '&';
        }

        return builder.ToString();
    }

    private static SortedDictionary<string, string> BuildHeaders(ChatRequest request, OpenAiChatProvider provider)
    {
        var headers = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = "application/json",
            ["Accept"] = "text/event-stream",
            ["User-Agent"] = provider.UserAgent,
        };

        if (request.Extra?.Headers is { } extraHeaders)
        {
            foreach (var (key, value) in extraHeaders)
            {
                headers[key] = value;
            }
        }

        if (provider.ExtraHeaders is { } providerHeaders)
        {
            foreach (var (key, value) in providerHeaders)
            {
                headers[key] = value;
            }
        }

        var keyHeaderPresent = headers.ContainsKey("x-api-key") || headers.ContainsKey("api-key");
        if (!keyHeaderPresent && provider.ApiKey is { Length: > 0 } apiKey && !headers.ContainsKey("Authorization"))
        {
            headers["Authorization"] = "Bearer " + apiKey;
        }

        return headers;
    }

    private static JsonObject BuildBody(ChatRequest request, OpenAiChatProvider provider, IAttachmentSource? attachments, List<NoticeInfo> notices)
    {
        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = BuildMessages(request, attachments),
            ["stream"] = request.Stream,
        };

        if (request.Stream)
        {
            body["stream_options"] = new JsonObject { ["include_usage"] = true };
        }

        if (request.Sampling is { } sampling)
        {
            AddIfPresent(body, "temperature", sampling.Temperature);
            AddIfPresent(body, "top_p", sampling.TopP);
            AddIfPresent(body, "frequency_penalty", sampling.FrequencyPenalty);
            AddIfPresent(body, "presence_penalty", sampling.PresencePenalty);
            AddIfPresent(body, "seed", sampling.Seed);
            AddIfPresent(body, "stop", sampling.Stop);
            if (sampling.MaxOutputTokens is { } maxTokens)
            {
                body[provider.UseMaxCompletionTokens ? "max_completion_tokens" : "max_tokens"] = maxTokens;
            }

            if (sampling.TopK is not null)
            {
                notices.Add(NoticeInfo.Of(NoticeCode.CapabilityStripped, ("dropped", "top_k")));
            }
        }

        if (request.Tools is { Count: > 0 } tools)
        {
            var array = new JsonArray();
            foreach (var tool in tools)
            {
                array.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["parameters"] = tool.Parameters?.DeepClone(),
                    },
                });
            }

            body["tools"] = array;
            if (request.ToolChoice is { } toolChoice)
            {
                body["tool_choice"] = toolChoice;
            }
        }
        else if (request.ToolChoice is not null)
        {
            notices.Add(NoticeInfo.Of(NoticeCode.CapabilityStripped, ("dropped", "tools")));
        }

        if (request.ResponseFormat is { } responseFormat && responseFormat.Type != ResponseFormatType.Text)
        {
            body["response_format"] = BuildResponseFormat(responseFormat);
        }

        if (request.Reasoning is { } reasoning && reasoning.Mode != ReasoningMode.Off)
        {
            var effort = reasoning.Mode == ReasoningMode.Budget ? EffortFromBudget(reasoning.BudgetTokens) : reasoning.Effort;
            if (effort is { } value)
            {
                body["reasoning_effort"] = value.ToString().ToLowerInvariant();
            }
        }

        if (provider.ExtraBody is { } providerBody)
        {
            foreach (var (key, value) in providerBody)
            {
                body[key] = value?.DeepClone();
            }
        }

        if (request.Extra?.Body is { } requestBody)
        {
            foreach (var (key, value) in requestBody)
            {
                body[key] = value?.DeepClone();
            }
        }

        return body;
    }

    private static JsonObject BuildResponseFormat(ResponseFormat format)
    {
        switch (format.Type)
        {
            case ResponseFormatType.JsonObject:
                return new JsonObject { ["type"] = "json_object" };
            case ResponseFormatType.JsonSchema:
                return new JsonObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JsonObject
                    {
                        ["name"] = format.Name ?? "response",
                        ["schema"] = format.Schema?.DeepClone(),
                        ["strict"] = true,
                    },
                };
            default:
                return new JsonObject { ["type"] = "text" };
        }
    }

    /// <summary>Reverse table of providers.md §3.1: budget → effort, upper bound inclusive.</summary>
    private static ReasoningEffort EffortFromBudget(int? budgetTokens) => budgetTokens switch
    {
        null => ReasoningEffort.Medium,
        < 2048 => ReasoningEffort.Low,
        < 8192 => ReasoningEffort.Medium,
        _ => ReasoningEffort.High,
    };

    private static JsonArray BuildMessages(ChatRequest request, IAttachmentSource? attachments)
    {
        var messages = new JsonArray();
        if (request.System is { Length: > 0 } system)
        {
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = system });
        }

        foreach (var message in request.Messages)
        {
            messages.Add(BuildMessage(message, attachments));
        }

        return messages;
    }

    private static JsonObject BuildMessage(ChatMessage message, IAttachmentSource? attachments)
    {
        var node = new JsonObject { ["role"] = message.Role };
        if (message is { Role: "tool", ToolCallId: { } toolCallId })
        {
            node["tool_call_id"] = toolCallId;
        }

        var toolCalls = new JsonArray();
        var contentParts = new JsonArray();
        var textOnly = new StringBuilder();
        var hasNonText = false;
        var contentAssigned = false;

        foreach (var part in message.Parts)
        {
            switch (part)
            {
                case TextPart text:
                    textOnly.Append(text.Text);
                    contentParts.Add(new JsonObject { ["type"] = "text", ["text"] = text.Text });
                    break;
                case ImagePart image:
                    hasNonText = true;
                    if (attachments is null)
                    {
                        throw new InvalidOperationException("an image part needs an attachment source; the canonical layer never carries base64");
                    }

                    var base64 = Convert.ToBase64String(attachments.Read(image.DataRef));
                    contentParts.Add(new JsonObject
                    {
                        ["type"] = "image_url",
                        ["image_url"] = new JsonObject
                        {
                            ["url"] = $"data:{image.Mime};base64,{base64}",
                            ["detail"] = image.Detail ?? "auto",
                        },
                    });
                    break;
                case FilePart file:
                    hasNonText = true;
                    contentParts.Add(new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = $"[attachment {file.FileName ?? file.DataRef} ({file.Mime})]",
                    });
                    break;
                case ToolCallPart toolCall:
                    toolCalls.Add(new JsonObject
                    {
                        ["id"] = toolCall.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = toolCall.Name,
                            ["arguments"] = CanonicalJson.Serialize(toolCall.Args),
                        },
                    });
                    break;
                case ToolResultPart toolResult:
                    node["content"] = CanonicalJson.Serialize(toolResult.Result);
                    contentAssigned = true;
                    break;
            }
        }

        if (toolCalls.Count > 0)
        {
            node["tool_calls"] = toolCalls;
            if (textOnly.Length > 0)
            {
                node["content"] = textOnly.ToString();
            }
        }
        else if (!contentAssigned)
        {
            node["content"] = hasNonText ? contentParts : textOnly.ToString();
        }

        return node;
    }

    private static void AddIfPresent<T>(JsonObject body, string name, T? value)
        where T : struct
    {
        if (value is not { } present)
        {
            return;
        }

        body[name] = JsonValue.Create(present);
    }

    private static void AddIfPresent(JsonObject body, string name, IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return;
        }

        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        body[name] = array;
    }
}
