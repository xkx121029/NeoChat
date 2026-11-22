using NeoChat.Protocol.Transport;

namespace NeoChat.Protocol.Normalize;

/// <summary>Provider protocol adapters translate provider frames into emitter calls. One per stream attempt.</summary>
public interface IChatStreamAdapter
{
    void OnFrame(SseFrame frame);

    /// <summary>Called once when the body ends without the adapter having terminated the stream.</summary>
    void OnEof();
}
