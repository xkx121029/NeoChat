using System.Collections.ObjectModel;
using System.Globalization;
using NeoChat.Protocol.Canonical;
using NeoChat.Protocol.Errors;
using NeoChat.Protocol.Events;
using NeoChat.Windows.Core.Errors;
using NeoChat.Windows.Core.Mvvm;
using NeoChat.Windows.Core.Storage;

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

    /// <summary>Label for a conversation whose title has not been derived from its first message yet.</summary>
    public const string DefaultSessionTitle = "新对话";

    private readonly IChatStreamClient _client;
    private readonly IFailureLog _failureLog;
    private readonly IConversationRepository? _repository;
    private string? _conversationId;
    private string _sessionTitle = DefaultSessionTitle;
    private CancellationTokenSource? _streamCancel;
    private string _composerText = string.Empty;
    private SessionStatus _status = SessionStatus.Idle;
    private ErrorPresentation? _error;
    private ProviderSettings _settings;
    private bool _disposed;

    public ChatSessionViewModel(IChatStreamClient client, IFailureLog failureLog, ProviderSettings settings)
        : this(client, failureLog, settings, null)
    {
    }

    /// <summary>
    /// With a repository, every turn lands in SQLite as it happens (each streamed fragment is written,
    /// not just the finished reply) and <see cref="OpenConversation"/> restores a stored history.
    /// Without one the view model behaves exactly as it did before persistence existed.
    /// </summary>
    public ChatSessionViewModel(
        IChatStreamClient client,
        IFailureLog failureLog,
        ProviderSettings settings,
        IConversationRepository? repository)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(failureLog);
        ArgumentNullException.ThrowIfNull(settings);

        _client = client;
        _failureLog = failureLog;
        _settings = settings;
        _repository = repository;
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

    /// <summary>Conversation name for the header capsule (Y54); an untitled conversation reads as 新对话.</summary>
    public string SessionTitle
    {
        get => _sessionTitle;
        private set => SetProperty(ref _sessionTitle, value);
    }

    /// <summary>The stored conversation this session writes into; null until one exists.</summary>
    public string? ConversationId
    {
        get => _conversationId;
        private set => SetProperty(ref _conversationId, value);
    }

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
        PersistUserMessage(user, text, isFirstMessage: Messages.Count == 1);
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

    /// <summary>
    /// Answers the same request again from the same parent: the reply is replaced by a new sibling branch
    /// (same <c>parent_id</c>, next <c>sibling_index</c>) and the abandoned row stays in the store, so the
    /// tree keeps both attempts and the new one becomes the active leaf.
    /// </summary>
    public async Task RegenerateAsync(ChatMessageItem? assistant)
    {
        if (IsStreaming || assistant is null || !assistant.IsAssistant)
        {
            return;
        }

        var index = Messages.IndexOf(assistant);
        if (index <= 0 || !Messages[index - 1].IsUser)
        {
            return;
        }

        var parent = Messages[index - 1];
        while (Messages.Count > index)
        {
            Messages.RemoveAt(Messages.Count - 1);
        }

        Error = null;
        await RunAsync(parent).ConfigureAwait(true);
    }

    /// <summary>
    /// Soft delete (DDL <c>deleted_at</c>): the message and the turns built on it leave the thread, while
    /// the rows stay in the store for the 30-day recycle bin.
    /// </summary>
    public void DeleteMessage(ChatMessageItem? message)
    {
        if (IsStreaming || message is null)
        {
            return;
        }

        var index = Messages.IndexOf(message);
        if (index < 0)
        {
            return;
        }

        if (_repository is not null && message.RowId is { } rowId)
        {
            _repository.SoftDeleteMessage(rowId);
        }

        while (Messages.Count > index)
        {
            Messages.RemoveAt(Messages.Count - 1);
        }
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

        if (_repository is null)
        {
            ConversationId = null;
            SessionTitle = DefaultSessionTitle;
            return;
        }

        // The conversation row exists from the moment a new chat is asked for, and an untouched one is
        // reused rather than piled up, so a restart never opens onto a wall of empty conversations.
        var conversation = _repository.EnsureOpenConversation();
        ConversationId = conversation.Id;
        SessionTitle = conversation.DisplayTitle;
    }

    /// <summary>Loads a stored conversation: its title and its active message branch, root first.</summary>
    public void OpenConversation(string conversationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        if (IsStreaming)
        {
            return;
        }

        ConversationId = conversationId;
        Messages.Clear();
        Error = null;
        Status = SessionStatus.Idle;

        if (_repository is null)
        {
            SessionTitle = DefaultSessionTitle;
            return;
        }

        SessionTitle = _repository.FindConversation(conversationId)?.DisplayTitle ?? DefaultSessionTitle;
        foreach (var row in _repository.LoadThread(conversationId))
        {
            Messages.Add(new ChatMessageItem(row.Role)
            {
                RowId = row.Id,
                Text = row.Text,
                ReasoningText = row.ReasoningText,
                Status = RestoredStatus(row.Status),
            });
        }
    }

    private void PersistUserMessage(ChatMessageItem user, string text, bool isFirstMessage)    {
        if (_repository is null)
        {
            return;
        }

        var autoTitle = isFirstMessage ? ConversationTitle.FromFirstMessage(text) : null;
        var row = _repository.AppendMessage(
            EnsureConversationId(),
            ChatRole.User,
            ParentIdFor(user),
            text,
            StoredMessageStatus.Complete,
            autoTitle);
        user.RowId = row.Id;
        if (autoTitle is not null)
        {
            SessionTitle = autoTitle;
        }
    }

    /// <summary>
    /// One streamed fragment, written the moment it arrives. Nothing is buffered until the turn ends:
    /// a process killed here leaves the text it had already received in the database.
    /// </summary>
    private void PersistDelta(ChatMessageItem assistant, string delta, bool reasoning)
    {
        if (_repository is null || _conversationId is null || assistant.RowId is null)
        {
            return;
        }

        if (reasoning)
        {
            _repository.AppendReasoning(assistant.RowId, delta);
            return;
        }

        _repository.AppendText(assistant.RowId, _conversationId, delta, assistant.Text);
    }

    private void PersistAssistantStart(ChatMessageItem assistant)
    {
        if (_repository is null)
        {
            return;
        }

        var row = _repository.AppendMessage(
            EnsureConversationId(),
            ChatRole.Assistant,
            ParentIdFor(assistant),
            string.Empty,
            StoredMessageStatus.Streaming);
        assistant.RowId = row.Id;
    }

    private void PersistAssistantEnd(
        ChatMessageItem assistant,
        StoredMessageStatus status,
        FinishReason? reason,
        string? errorJson)
    {
        if (_repository is null || _conversationId is null || assistant.RowId is null)
        {
            return;
        }

        _repository.SetMessageStatus(
            assistant.RowId,
            status,
            reason is null ? null : FinishReasonWire.ToWire(reason.Value),
            errorJson);

        // The leaf is what the tree resumes from, so a retried reply becomes the active branch and the
        // abandoned sibling stays in the database without being rendered again.
        if (assistant.IsAssistant)
        {
            _repository.SetActiveLeaf(_conversationId, assistant.RowId);
        }
    }

    private string EnsureConversationId()
    {
        ConversationId ??= _repository!.EnsureOpenConversation().Id;
        return ConversationId;
    }

    private string? ParentIdFor(ChatMessageItem item)
    {
        var index = Messages.IndexOf(item);
        for (var previous = index - 1; previous >= 0; previous--)
        {
            if (Messages[previous].RowId is { } rowId)
            {
                return rowId;
            }
        }

        return null;
    }

    private static MessageStatus RestoredStatus(StoredMessageStatus status) => status switch
    {
        StoredMessageStatus.Complete => MessageStatus.Complete,
        StoredMessageStatus.Failed => MessageStatus.Failed,
        _ => MessageStatus.Stopped,
    };

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
        PersistAssistantStart(assistant);

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
                    PersistDelta(assistant, delta, reasoning: false);
                    AdvanceToStreaming(assistant, user);
                }

                break;
            case StreamEventKind.ReasoningDelta:
                if (streamEvent.Text is { Length: > 0 } reasoning)
                {
                    assistant.AppendReasoning(reasoning);
                    PersistDelta(assistant, reasoning, reasoning: true);
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
            PersistAssistantEnd(assistant, StoredMessageStatus.Aborted, FinishReason.AbortedUser, null);
            return;
        }

        assistant.Status = MessageStatus.Complete;
        Status = SessionStatus.Idle;
        PersistAssistantEnd(assistant, StoredMessageStatus.Complete, reason, null);
    }

    private void MarkStopped(ChatMessageItem assistant, ChatMessageItem user)
    {
        user.Status = MessageStatus.Complete;
        if (assistant.IsStreaming)
        {
            assistant.Status = MessageStatus.Stopped;
        }

        Status = SessionStatus.Stopped;
        PersistAssistantEnd(assistant, StoredMessageStatus.Aborted, FinishReason.AbortedUser, null);
    }

    private void Fail(ChatMessageItem assistant, ProviderError error)
    {
        assistant.Error = error;
        assistant.Status = MessageStatus.Failed;
        Error = ErrorPresentation.From(error);
        Status = SessionStatus.Failed;
        _failureLog.Record(error);
        PersistAssistantEnd(assistant, StoredMessageStatus.Failed, null, StreamEventJson.ErrorToJson(error));
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
