using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NeoChat.Windows.Core.Chat;

namespace NeoChat.Windows.Views;

/// <summary>Picks the bubble template for user rows and the borderless template for assistant rows.</summary>
public sealed partial class ChatMessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? User { get; set; }

    public DataTemplate? Assistant { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) =>
        item is ChatMessageItem message && message.IsUser ? User : Assistant;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
