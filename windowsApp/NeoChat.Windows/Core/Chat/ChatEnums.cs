namespace NeoChat.Windows.Core.Chat;

/// <summary>Who authored a message. M1 renders no avatars or bylines for either role.</summary>
public enum ChatRole
{
    User,
    Assistant,
}

/// <summary>
/// Per-message lifecycle. <see cref="Sending"/> is the optimistic state of a user bubble before the
/// provider echoes anything (design Y18) and the pre-first-token state of an assistant message (ST04).
/// </summary>
public enum MessageStatus
{
    Sending,
    Streaming,
    Complete,
    Stopped,
    Failed,
}

/// <summary>Session-level status shown in the header capsule (ST13: every dot carries its own text).</summary>
public enum SessionStatus
{
    Idle,
    Connecting,
    Streaming,
    Stopped,
    Failed,
}
