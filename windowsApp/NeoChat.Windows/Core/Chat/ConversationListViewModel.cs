using System.Collections.ObjectModel;
using System.Globalization;
using NeoChat.Windows.Core.Mvvm;
using NeoChat.Windows.Core.Storage;

namespace NeoChat.Windows.Core.Chat;

/// <summary>
/// The conversation rail's projection of a stored conversation: a title, the last-message time as the
/// user reads it, and the id the page switches on.
/// </summary>
public sealed record ConversationListItem(string Id, string Title, string TimeLabel);

/// <summary>
/// Left rail content: the conversations ordered by <c>last_message_at DESC</c> (the order
/// <see cref="IConversationRepository.ListConversations"/> returns) and the empty state the view binds
/// to. All wording lives here so the XAML carries no literal text of its own.
/// </summary>
public sealed class ConversationListViewModel : ObservableObject
{
    public const string EmptyTitle = "还没有会话";
    public const string EmptyHint = "点「新对话」开始";
    public const string NewConversationLabel = "新对话";
    public const string NoTimeLabel = "—";

    private readonly IConversationRepository? _repository;
    private readonly TimeProvider _time;
    private string? _selectedId;

    public ConversationListViewModel(IConversationRepository? repository, TimeProvider? time = null)
    {
        _repository = repository;
        _time = time ?? TimeProvider.System;
    }

    public ObservableCollection<ConversationListItem> Items { get; } = [];


    public bool IsEmpty => Items.Count == 0;

    public string? SelectedId
    {
        get => _selectedId;
        private set => SetProperty(ref _selectedId, value);
    }

    /// <summary>Reloads the rail from the database; the selected row stays selected by id.</summary>
    public void Refresh(string? selectedId = null)
    {
        if (_repository is null)
        {
            return;
        }

        var keep = selectedId ?? SelectedId;
        Items.Clear();
        foreach (var conversation in _repository.ListConversations())
        {
            Items.Add(new ConversationListItem(
                conversation.Id,
                conversation.DisplayTitle,
                conversation.LastMessageAt is { } last
                    ? ConversationTime.Label(last, _time.GetUtcNow())
                    : NoTimeLabel));
        }

        SelectedId = keep is not null && Items.Any(item => item.Id == keep) ? keep : null;
        OnPropertyChanged(nameof(IsEmpty));
    }

    public void Select(string? conversationId) => SelectedId = conversationId;
}

/// <summary>
/// "When was this conversation last touched", in the short form a rail row has room for: clock time
/// today, the word for yesterday, month-day this year, full date beyond it.
/// </summary>
public static class ConversationTime
{
    public const string YesterdayLabel = "昨天";

    public static string Label(long epochMilliseconds, DateTimeOffset now)
    {
        var local = DateTimeOffset.FromUnixTimeMilliseconds(epochMilliseconds).ToLocalTime();
        var today = now.ToLocalTime().Date;
        if (local.Date == today)
        {
            return local.ToString("HH:mm", CultureInfo.CurrentCulture);
        }

        if (local.Date == today.AddDays(-1))
        {
            return YesterdayLabel;
        }

        return local.Year == today.Year
            ? local.ToString("MM-dd", CultureInfo.CurrentCulture)
            : local.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);
    }
}
