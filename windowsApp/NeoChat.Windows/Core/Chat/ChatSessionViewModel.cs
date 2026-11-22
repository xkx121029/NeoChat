using System.Collections.ObjectModel;
using System.Globalization;
using NeoChat.Protocol.Canonical;
using NeoChat.Protocol.Errors;
using NeoChat.Protocol.Events;
using NeoChat.Windows.Core.Errors;
using NeoChat.Windows.Core.Mvvm;

namespace NeoChat.Windows.Core.Chat;

/// <summary>
/// The whole of "一场普通对话" without any UI framework: it owns the message list, folds a
/// <see cref="StreamEvent"/> sequence into message/session states, and persists failures. Views bind to
/// it; tests drive it through a scripted <see cref="IChatStreamClient"/>.
/// </summary>
public sealed class ChatSessionViewModel : ObservableObject, IDisposable
{
    /// <summary>spec/errors.md §3.2. <c>ErrorCatalog</c> registers this code but exposes no const for it.</summary>
    private const string AuthMissingKeyCode = "AUTH_MISSING_KEY";

    private readonly IChatStreamClient _client;
    private readonly IFailureLog _failureLog;
    private CancellationTokenSource? _streamCancel;
    private string _composerText = string.Empty;
    private SessionStatus _status = SessionStatus.Idle;
    private ErrorPresentation? _error;
    private ProviderSettings _settings;
    private bool _disposed;

    public ChatSessionViewModel(IChatStreamClient client, IFailureLog failureLog, ProviderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(failureLog);
        ArgumentNullException.ThrowIfNull(settings);

        _client = client;
        _failureLog = failureLog;
        _settings = settings;
        Messages.CollectionChanged += OnMessagesChanged;

        SendCommand = new AsyncRelayCommand(SendAsync, () => CanSend);
        StopCommand = new RelayCommand(Stop, () => IsStreaming);
        RetryCommand = new AsyncRelayCommand(RetryAsync, () => CanRetry);
        DismissErrorCommand = new RelayCommand(DismissError, () => HasError);
    }

    public ObservableCollection<ChatMessageItem> Messages { get; } = [];

    public AsyncRelayCommand SendCommand { get; }

    public RelayCommand StopCommand { get; }

    public AsyncRelayCommand RetryCommand { get; }

    public RelayCommand DismissErrorCommand { get; }

    /// <summary>Conversation name for the header capsule (Y54). No conversation store exists in M1.</summary>
    public string SessionTitle { get; } = "新对话";

    public ProviderSettings Settings => _settings;

    public string ModelLabel => string.IsNullOrWhiteSpace(_settings.Model) ? "未配置模型" : _settings.Model;

    public SessionStatus Status
    {
        get => _status;
        private set
        {
            if (!SetProperty(ref _status, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsStreaming));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanRetry));
            RefreshCommands();
        }
    }

    public string StatusText => _status switch
    {
        SessionStatus.Connecting => "连接中",
        SessionStatus.Streaming => "流式中",
        SessionStatus.Stopped => "已停止",
        SessionStatus.Failed => "出错了",
        _ => "就绪",
    };

    public bool IsStreaming => _status is SessionStatus.Connecting or SessionStatus.Streaming;

    public ErrorPresentation? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
            {
                OnPropertyChanged(nameof(HasError));
                RefreshCommands();
            }
        }
    }

    public bool HasError => _error is not null;

    public string ComposerText
    {
        get => _composerText;
        set
        {
            if (!SetProperty(ref _composerText, value ?? string.Empty))
            {
                return;
            }

            OnPropertyChanged(nameof(CharacterCount));
            OnPropertyChanged(nameof(CharacterCountText));
            OnPropertyChanged(nameof(ShowCharacterCount));
            OnPropertyChanged(nameof(CanSend));
            RefreshCommands();
        }
    }

    /// <summary>Grapheme count, so an emoji or a combining mark counts as one character.</summary>
    public int CharacterCount => new StringInfo(_composerText).LengthInTextElements;

    public string CharacterCountText =>
        CharacterCount == 0 ? string.Empty : CharacterCount.ToString(CultureInfo.InvariantCulture) + " 字符";

    public bool ShowCharacterCount => CharacterCount > 0;

    public bool CanSend => !IsStreaming && !string.IsNullOrWhiteSpace(_composerText);

    public bool CanRetry => !IsStreaming && Messages.Any(m => m.IsUser);

    public bool HasMessages => Messages.Count > 0;

    /// <summary>Empty state shows until the conversation has its first message (ST16–ST26).</summary>
    public bool ShowEmptyState => Messages.Count == 0 && !IsStreaming;

    /// <summary>Header status dot colour role (ST13: ok / busy / warn / error / idle).</summary>
    public string StatusRole => _status switch
    {
        SessionStatus.Connecting or SessionStatus.Streaming => "busy",
        SessionStatus.Stopped => "warn",
        SessionStatus.Failed => "error",
        _ => "idle",
    };

    public void ApplyProviderSettings(ProviderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(ModelLabel));
    }

    /// <summary>Appends the composer text as a user message and streams the reply.</summary>
    public async Task SendAsync()
    {
        if (!CanSend)
        {
            return;
        }

        var text = ComposerText.Trim();
        ComposerText = string.Empty;
        var user = new ChatMessageItem(ChatRole.User) { Text = text, Status = MessageStatus.Sending };
        Messages.Add(user);
        await RunAsync(user).ConfigureAwait(true);
    }

    /// <summary>Drops the failed rows after the last user message and streams that request again.</summary>
    public async Task RetryAsync()
    {
        if (!CanRetry)
        {
            return;
        }

        var lastUser = Messages.Last(m => m.IsUser);
        while (Messages.Count > 0 && !ReferenceEquals(Messages[^1], lastUser))
        {
            Messages.RemoveAt(Messages.Count - 1);
        }

        Error = null;
        await RunAsync(lastUser).ConfigureAwait(true);
    }

    /// <summary>Header "new chat" key: drops the visible conversation once nothing is streaming.</summary>
    public void StartNewConversation()
    {
        if (IsStreaming)
        {
            return;
        }

        Messages.Clear();
        Error = null;
        Status = SessionStatus.Idle;
    }

    /// <summary>User cancel: closes the connection and lands the reply in <see cref="MessageStatus.Stopped"/>.</summary>
    public void Stop() => _streamCancel?.Cancel();

    public void DismissError() => Error = null;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _streamCancel?.Dispose();
        Messages.CollectionChanged -= OnMessagesChanged;
    }

    private async Task RunAsync(ChatMessageItem user)
    {
        var assistant = new ChatMessageItem(ChatRole.Assistant) { Status = MessageStatus.Sending };
        Messages.Add(assistant);
        Error = null;

        if (!_settings.IsConfigured)
        {
            Fail(assistant, ProtocolErrors.Create(
                AuthMissingKeyCode,
                ProtocolErrors.NewTraceId(),
                provider: _settings.ProtocolLabel,
                model: string.IsNullOrWhiteSpace(_settings.Model) ? null : _settings.Model));
            return;
        }

        var request = BuildRequest(assistant);
        using var cancel = new CancellationTokenSource();
        _streamCancel = cancel;
        Status = SessionStatus.Connecting;
        try
        {
            await foreach (var streamEvent in _client.StreamAsync(request, cancel.Token).ConfigureAwait(true))
            {
                Apply(assistant, user, streamEvent);
                if (streamEvent.IsTerminal)
                {
                    break;
                }
            }

            if (assistant.IsStreaming)
            {
                Fail(assistant, ErrorMapper.Incomplete(Context(), assistant.Text.Length));
            }
        }
        catch (OperationCanceledException)
        {
            MarkStopped(assistant, user);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Fail(assistant, MapTransportFailure(exception));
        }
        finally
        {
            if (ReferenceEquals(_streamCancel, cancel))
            {
                _streamCancel = null;
            }

            user.Status = MessageStatus.Complete;
            RefreshCommands();
        }
    }

    private void Apply(ChatMessageItem assistant, ChatMessageItem user, StreamEvent streamEvent)
    {
        switch (streamEvent.Kind)
        {
            case StreamEventKind.Delta:
                if (streamEvent.Text is { Length: > 0 } delta)
                {
                    assistant.AppendText(delta);
                    AdvanceToStreaming(assistant, user);
                }

                break;
            case StreamEventKind.ReasoningDelta:
                if (streamEvent.Text is { Length: > 0 } reasoning)
                {
                    assistant.AppendReasoning(reasoning);
                    AdvanceToStreaming(assistant, user);
                }

                break;
            case StreamEventKind.Usage:
                assistant.Usage = streamEvent.Usage;
                break;
            case StreamEventKind.Notice:
                if (streamEvent.Notice is { } notice)
                {
                    assistant.AddNotice(notice);
                }

                break;
            case StreamEventKind.ToolCallDelta:
            case StreamEventKind.ToolCallEnd:
                // No tools in the M1 closure; the events are consumed and not rendered.
                break;
            case StreamEventKind.Done:
                Complete(assistant, user, streamEvent.FinishReason);
                break;
            case StreamEventKind.Failed:
                Fail(assistant, streamEvent.Error ?? ErrorMapper.Internal(Context(), "failed event without an error object"));
                break;
        }
    }

    private void AdvanceToStreaming(ChatMessageItem assistant, ChatMessageItem user)
    {
        user.Status = MessageStatus.Complete;
        if (assistant.Status == MessageStatus.Sending)
        {
            assistant.Status = MessageStatus.Streaming;
        }

        if (Status != SessionStatus.Streaming)
        {
            Status = SessionStatus.Streaming;
        }
    }

    private void Complete(ChatMessageItem assistant, ChatMessageItem user, FinishReason? reason)
    {
        user.Status = MessageStatus.Complete;
        if (reason == FinishReason.AbortedUser)
        {
            assistant.Status = MessageStatus.Stopped;
            Status = SessionStatus.Stopped;
            return;
        }

        assistant.Status = MessageStatus.Complete;
        Status = SessionStatus.Idle;
    }

    private void MarkStopped(ChatMessageItem assistant, ChatMessageItem user)
    {
        user.Status = MessageStatus.Complete;
        if (assistant.IsStreaming)
        {
            assistant.Status = MessageStatus.Stopped;
        }

        Status = SessionStatus.Stopped;
    }

    private void Fail(ChatMessageItem assistant, ProviderError error)
    {
        assistant.Error = error;
        assistant.Status = MessageStatus.Failed;
        Error = ErrorPresentation.From(error);
        Status = SessionStatus.Failed;
        _failureLog.Record(error);
    }

    private ChatRequest BuildRequest(ChatMessageItem streaming)
    {
        var messages = new List<ChatMessage>(Messages.Count);
        foreach (var item in Messages)
        {
            if (ReferenceEquals(item, streaming) || item.Status == MessageStatus.Failed || !item.HasText)
            {
                continue;
            }

            messages.Add(new ChatMessage(
                item.IsUser ? "user" : "assistant",
                [new TextPart(item.Text)]));
        }

        return new ChatRequest { Model = _settings.Model, Messages = messages, Stream = true };
    }

    private ErrorContext Context() => new()
    {
        Provider = _settings.ProtocolLabel,
        Model = string.IsNullOrWhiteSpace(_settings.Model) ? null : _settings.Model,
    };

    private ProviderError MapTransportFailure(Exception exception) => exception switch
    {
        HttpRequestException or IOException or ObjectDisposedException => ErrorMapper.ConnectionReset(Context(), exception.Message),
        _ => ErrorMapper.Internal(Context(), exception.Message),
    };

    private void OnMessagesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasMessages));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(CanRetry));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        SendCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        RetryCommand.RaiseCanExecuteChanged();
        DismissErrorCommand.RaiseCanExecuteChanged();
    }
}
