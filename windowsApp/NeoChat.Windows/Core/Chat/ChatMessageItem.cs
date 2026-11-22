using NeoChat.Protocol.Events;
using NeoChat.Windows.Core.Mvvm;

namespace NeoChat.Windows.Core.Chat;

/// <summary>
/// One row of the conversation. Owned and mutated by <see cref="ChatSessionViewModel"/>; the view only
/// reads it. Derived flags exist so the XAML templates stay free of converters.
/// </summary>
public sealed class ChatMessageItem : ObservableObject
{
    private string _text = string.Empty;
    private string _reasoningText = string.Empty;
    private MessageStatus _status = MessageStatus.Sending;
    private ProviderError? _error;
    private UsageInfo? _usage;
    private bool _isReasoningExpanded;

    public ChatMessageItem(ChatRole role) => Role = role;

    public ChatRole Role { get; }

    /// <summary>Database row id once the message is persisted; null when no store is attached.</summary>
    public string? RowId { get; internal set; }

    public bool IsUser => Role == ChatRole.User;

    public bool IsAssistant => Role == ChatRole.Assistant;

    public string Text
    {
        get => _text;
        internal set
        {
            if (SetProperty(ref _text, value))
            {
                OnPropertyChanged(nameof(HasText));
                OnPropertyChanged(nameof(ShowStreamingCursor));
                OnPropertyChanged(nameof(ShowWaitingIndicator));
            }
        }
    }

    public bool HasText => _text.Length > 0;

    public string ReasoningText
    {
        get => _reasoningText;
        internal set
        {
            if (SetProperty(ref _reasoningText, value))
            {
                OnPropertyChanged(nameof(HasReasoning));
                OnPropertyChanged(nameof(ReasoningLabel));
            }
        }
    }

    public bool HasReasoning => _reasoningText.Length > 0;

    /// <summary>Thinking-row label (ST45–ST50). Only meaningful while <see cref="HasReasoning"/>.</summary>
    public string ReasoningLabel => Status is MessageStatus.Sending or MessageStatus.Streaming ? "思考中" : "思考过程";

    public MessageStatus Status
    {
        get => _status;
        internal set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(IsStreaming));
                OnPropertyChanged(nameof(ShowStreamingCursor));
                OnPropertyChanged(nameof(ShowWaitingIndicator));
                OnPropertyChanged(nameof(ShowInterruptedMarker));
                OnPropertyChanged(nameof(IsFailed));
                OnPropertyChanged(nameof(ReasoningLabel));
                OnPropertyChanged(nameof(StateLabel));
            }
        }
    }

    public bool IsStreaming => _status is MessageStatus.Sending or MessageStatus.Streaming;

    /// <summary>Does the row still take tokens? Drives the streaming cursor (ST01).</summary>
    public bool ShowStreamingCursor => IsAssistant && IsStreaming && HasText;

    /// <summary>Before the first text token the row shows the waiting indicator instead (ST04).</summary>
    public bool ShowWaitingIndicator => IsAssistant && IsStreaming && !HasText;

    /// <summary>Interrupted marker at the end of the body (Y26).</summary>
    public bool ShowInterruptedMarker => _status == MessageStatus.Stopped;

    public bool IsFailed => _status == MessageStatus.Failed;

    /// <summary>Short state word for automation and accessibility; never shown inline.</summary>
    public string StateLabel => _status switch
    {
        MessageStatus.Sending => "sending",
        MessageStatus.Streaming => "streaming",
        MessageStatus.Complete => "complete",
        MessageStatus.Stopped => "stopped",
        _ => "failed",
    };

    public ProviderError? Error
    {
        get => _error;
        internal set
        {
            if (SetProperty(ref _error, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => _error is not null;

    public UsageInfo? Usage
    {
        get => _usage;
        internal set => SetProperty(ref _usage, value);
    }

    public IReadOnlyList<NoticeInfo> Notices { get; private set; } = Array.Empty<NoticeInfo>();

    /// <summary>Thinking row expanded/collapsed. Starts collapsed (ST08: the row is a window until tapped).</summary>
    public bool IsReasoningExpanded
    {
        get => _isReasoningExpanded;
        set => SetProperty(ref _isReasoningExpanded, value);
    }

    internal void AppendText(string delta) => Text = _text + delta;

    internal void AppendReasoning(string delta) => ReasoningText = _reasoningText + delta;

    internal void AddNotice(NoticeInfo notice) => Notices = [.. Notices, notice];
}
