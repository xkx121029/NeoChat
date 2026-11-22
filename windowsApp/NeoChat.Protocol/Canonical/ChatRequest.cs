using System.Text.Json.Nodes;

namespace NeoChat.Protocol.Canonical;

/// <summary>Part types are a closed set (spec/providers.md §2 rule 1).</summary>
public abstract record ContentPart;

public sealed record TextPart(string Text) : ContentPart;

public sealed record ImagePart(string Mime, string DataRef, string? Detail = null) : ContentPart;

public sealed record FilePart(string Mime, string DataRef, string? FileName = null) : ContentPart;

public sealed record ToolCallPart(string Id, string Name, JsonNode? Args) : ContentPart;

public sealed record ToolResultPart(bool Ok, JsonNode? Result) : ContentPart;

public sealed record ChatMessage(string Role, IReadOnlyList<ContentPart> Parts, string? ToolCallId = null);

public sealed record ToolDefinition(string Name, string? Description, JsonNode? Parameters);

public enum ReasoningMode
{
    Off,
    Effort,
    Budget,
}

public enum ReasoningEffort
{
    Low,
    Medium,
    High,
}

/// <summary><c>null</c> means "not specified, use the provider default" (§2 rule 3).</summary>
public sealed record Sampling
{
    public double? Temperature { get; init; }

    public double? TopP { get; init; }

    public int? TopK { get; init; }

    public int? MaxOutputTokens { get; init; }

    public IReadOnlyList<string>? Stop { get; init; }

    public int? Seed { get; init; }

    public double? FrequencyPenalty { get; init; }

    public double? PresencePenalty { get; init; }
}

public sealed record Reasoning
{
    public ReasoningMode Mode { get; init; } = ReasoningMode.Off;

    public ReasoningEffort? Effort { get; init; }

    public int? BudgetTokens { get; init; }

    public bool IncludeSummary { get; init; }
}

public enum ResponseFormatType
{
    Text,
    JsonObject,
    JsonSchema,
}

public sealed record ResponseFormat
{
    public ResponseFormatType Type { get; init; } = ResponseFormatType.Text;

    public string? Name { get; init; }

    public JsonNode? Schema { get; init; }
}

/// <summary>The single escape hatch of the canonical layer (§2 rule 5).</summary>
public sealed record ExtraPayload
{
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    public IReadOnlyDictionary<string, string>? Query { get; init; }

    public JsonObject? Body { get; init; }
}

/// <summary>Canonical ChatRequest (spec/providers.md §2).</summary>
public sealed record ChatRequest
{
    public required string Model { get; init; }

    public string? System { get; init; }

    public IReadOnlyList<ChatMessage> Messages { get; init; } = Array.Empty<ChatMessage>();

    public IReadOnlyList<ToolDefinition>? Tools { get; init; }

    public string? ToolChoice { get; init; }

    public Sampling? Sampling { get; init; }

    public Reasoning? Reasoning { get; init; }

    public ResponseFormat? ResponseFormat { get; init; }

    public bool Stream { get; init; } = true;

    public ExtraPayload? Extra { get; init; }
}
