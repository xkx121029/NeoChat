using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NeoChat.Windows.Views.Markdown;

/// <summary>
/// The block list drives <see cref="ItemsRepeater"/> element recycling by §4.6 key rather than by index,
/// so a streaming block keeps its element when neighbours are appended.
/// </summary>
internal sealed class MarkdownBlockList : ObservableCollection<MarkdownBlockItem>, IKeyIndexMapping
{
    public string KeyFromIndex(int index) => this[index].Key;

    public int IndexFromKey(string key)
    {
        for (var index = 0; index < Count; index++)
        {
            if (string.Equals(this[index].Key, key, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}

/// <summary>Creates and recycles <see cref="MarkdownBlockView"/> instances for the repeater.</summary>
internal sealed class MarkdownBlockFactory : IElementFactory
{
    private readonly Stack<MarkdownBlockView> _pool = [];

    public UIElement GetElement(ElementFactoryGetArgs args)
    {
        var view = _pool.Count > 0 ? _pool.Pop() : new MarkdownBlockView();
        if (args.Data is MarkdownBlockItem item)
        {
            view.Bind(item);
        }

        return view;
    }

    public void RecycleElement(ElementFactoryRecycleArgs args)
    {
        if (args.Element is MarkdownBlockView view)
        {
            view.Unbind();
            _pool.Push(view);
        }
    }
}
