namespace NeoChat.Windows.Core.Storage;

/// <summary>
/// The four values the frozen DDL allows in <c>message.status</c>. It is deliberately not the UI's
/// <see cref="Chat.MessageStatus"/>: <c>sending</c> is an optimistic view state that never reaches the
/// database, and <c>stopped</c> is the UI word for what the schema stores as <c>aborted</c>.
/// </summary>
public enum StoredMessageStatus
{
    Streaming,
    Complete,
    Failed,
    Aborted,
}

public static class StoredMessageStatusWire
{
    public const string Streaming = "streaming";
    public const string Complete = "complete";
    public const string Failed = "failed";
    public const string Aborted = "aborted";

    public static string ToWire(StoredMessageStatus status) => status switch
    {
        StoredMessageStatus.Streaming => Streaming,
        StoredMessageStatus.Complete => Complete,
        StoredMessageStatus.Failed => Failed,
        StoredMessageStatus.Aborted => Aborted,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unknown stored message status"),
    };

    public static StoredMessageStatus FromWire(string wire) => wire switch
    {
        Streaming => StoredMessageStatus.Streaming,
        Complete => StoredMessageStatus.Complete,
        Failed => StoredMessageStatus.Failed,
        Aborted => StoredMessageStatus.Aborted,
        _ => throw new InvalidOperationException("database holds unknown message.status: " + wire),
    };
}
