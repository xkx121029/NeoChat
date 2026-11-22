using NeoChat.Protocol.Diagnostics;
using NeoChat.Protocol.Errors;
using NeoChat.Protocol.Pipeline;

namespace NeoChat.Protocol.Normalize;

/// <summary>
/// The only place that branches on protocol family. Callers pass a <see cref="ProtocolFamily"/>;
/// Gemini, Anthropic and Responses adapters land in M2.
/// </summary>
public static class ChatStreamAdapterFactory
{
    public static IChatStreamAdapter Create(
        ProtocolFamily protocol,
        ChatStreamOptions options,
        StreamEventEmitter emitter,
        StreamTrace trace,
        ErrorContext context) => protocol switch
    {
        ProtocolFamily.OpenAiChat or ProtocolFamily.OpenAiCompatible or ProtocolFamily.OpenRouter =>
            new OpenAiChatAdapter(options, emitter, trace, context),
        _ => throw new NotSupportedException($"no M1 adapter for protocol family {protocol}"),
    };
}
