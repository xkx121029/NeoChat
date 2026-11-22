using NeoChat.Protocol.Canonical;
using NeoChat.Protocol.Events;

namespace NeoChat.Windows.Core.Chat;

/// <summary>Endpoint + model the chat session sends to. Kept in memory for M1: no key ever reaches disk.</summary>
public sealed record ProviderSettings
{
    public string BaseUrl { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string ProtocolLabel { get; init; } = "openai_chat";

    /// <summary>A key is optional (local OpenAI-compatible servers); base URL and model are not.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Model);

    public static ProviderSettings Unconfigured { get; } = new();

    /// <summary>
    /// Reads <c>NEOCHAT_BASE_URL</c>, <c>NEOCHAT_API_KEY</c> and <c>NEOCHAT_MODEL</c> from the process
    /// environment. Values live in the user's environment only; nothing is written to disk.
    /// </summary>
    public static ProviderSettings FromEnvironment() => new()
    {
        BaseUrl = Environment.GetEnvironmentVariable("NEOCHAT_BASE_URL") ?? string.Empty,
        ApiKey = Environment.GetEnvironmentVariable("NEOCHAT_API_KEY") ?? string.Empty,
        Model = Environment.GetEnvironmentVariable("NEOCHAT_MODEL") ?? string.Empty,
    };
}

/// <summary>
/// One stream attempt. The Windows implementation wraps the protocol layer's
/// <see cref="NeoChat.Protocol.Pipeline.ChatStreamSession"/>; tests substitute a scripted sequence.
/// </summary>
public interface IChatStreamClient
{
    IAsyncEnumerable<StreamEvent> StreamAsync(ChatRequest request, CancellationToken cancellationToken);
}
