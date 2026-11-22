using System.Collections.Specialized;
using NeoChat.Windows.Core.Chat;
using NeoChat.Windows.Core.Errors;
using NeoChat.Protocol.Events;

namespace NeoChat.Windows.Core.Tests;

/// <summary>
/// Records what the view would see: session status transitions and, per message row, the state
/// sequence. Everything is observed through INotifyPropertyChanged, so the assertions cover the same
/// surface the XAML binds to.
/// </summary>
internal sealed class StateRecorder
{
    private readonly Dictionary<ChatMessageItem, List<string>> _messages = [];

    public List<string> Session { get; } = [];

    public IReadOnlyList<string> For(ChatMessageItem item) =>
        _messages.TryGetValue(item, out var states) ? states : [];

    public string ForIndex(ChatSessionViewModel vm, int index) => string.Join("→", For(vm.Messages[index]));

    public static StateRecorder Attach(ChatSessionViewModel vm)
    {
        var recorder = new StateRecorder();
        recorder.Session.Add(vm.Status.ToString());
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatSessionViewModel.Status))
            {
                recorder.Session.Add(vm.Status.ToString());
            }
        };
        vm.Messages.CollectionChanged += recorder.OnCollectionChanged;
        foreach (var item in vm.Messages)
        {
            recorder.Track(item);
        }

        return recorder;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var item in e.NewItems?.Cast<ChatMessageItem>() ?? [])
        {
            Track(item);
        }
    }

    private void Track(ChatMessageItem item)
    {
        var states = new List<string> { item.Status.ToString() };
        _messages[item] = states;
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatMessageItem.Status))
            {
                states.Add(item.Status.ToString());
            }
        };
    }
}

internal sealed class InMemoryFailureLog : IFailureLog
{
    public List<ProviderError> Entries { get; } = [];

    public string Location => "(memory)";

    public void Record(ProviderError error) => Entries.Add(error);
}

internal sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
