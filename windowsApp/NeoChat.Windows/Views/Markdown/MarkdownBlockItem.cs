using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using NeoChat.Windows.Core.Design;
using NeoChat.Windows.Core.Render;
using Windows.UI;

namespace NeoChat.Windows.Views.Markdown;

/// <summary>
/// One block of the model, addressed by its §4.6 id. Streaming replaces the block in place, so the view
/// bound to this item is never torn down while the message is still arriving.
/// </summary>
internal sealed class MarkdownBlockItem : INotifyPropertyChanged
{
    public MarkdownBlockItem(Block block) => Block = block;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Stable view key: <c>b0</c>, <c>b1</c>, … (§4.6).</summary>
    public string Key => Block.Id;

    public Block Block { get; private set; }

    /// <summary>Streaming caret, owned by <see cref="MarkdownHost"/> and attached to the last block only.</summary>
    public Microsoft.UI.Xaml.UIElement? Cursor { get; private set; }

    public void Update(Block block)
    {
        if (ReferenceEquals(Block, block))
        {
            return;
        }

        Block = block;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Block)));
    }

    public void SetCursor(Microsoft.UI.Xaml.UIElement? cursor)
    {
        if (ReferenceEquals(Cursor, cursor))
        {
            return;
        }

        Cursor = cursor;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Cursor)));
    }
}

/// <summary>Brush and text helpers shared by every block view. Values only ever come from <see cref="Tokens"/>.</summary>
internal static class MarkdownLook
{
    internal static SolidColorBrush Brush(uint argb) => new(Color.FromArgb(
        (byte)((argb >> 24) & 0xFF),
        (byte)((argb >> 16) & 0xFF),
        (byte)((argb >> 8) & 0xFF),
        (byte)(argb & 0xFF)));

    internal static readonly SolidColorBrush Ink = Brush(Tokens.Palette.L09Ink);
    internal static readonly SolidColorBrush InkMuted = Brush(Tokens.Palette.L10InkMuted);
    internal static readonly SolidColorBrush InkFaint = Brush(Tokens.Palette.L11InkFaint);
    internal static readonly SolidColorBrush InkGhost = Brush(Tokens.Palette.L12InkGhost);
    internal static readonly SolidColorBrush Rule = Brush(Tokens.Palette.L14RuleLight);
    internal static readonly SolidColorBrush Seed = Brush(Tokens.Palette.A01Seed);
    internal static readonly SolidColorBrush Danger = Brush(Tokens.Palette.S07Danger);

    internal static readonly SolidColorBrush CodeWellBg = Brush(Tokens.Palette.M13CodeWellBg);
    internal static readonly SolidColorBrush CodeWellHeader = Brush(Tokens.Palette.M14CodeWellHeader);
    internal static readonly SolidColorBrush CodeWellFg = Brush(Tokens.Palette.M15CodeWellFg);
    internal static readonly SolidColorBrush CodeWellBorder = Brush(Tokens.Palette.M16CodeWellBorder);
    internal static readonly SolidColorBrush LineNumberFg = Brush(Tokens.Palette.M17LineNumberFg);

    internal static readonly FontFamily Sans = new(Tokens.Type.SansFamily);
    internal static readonly FontFamily Mono = new(Tokens.Type.MonoFamily);

    internal static (double Size, double LineHeight) HeadingScale(int level) => level switch
    {
        1 => (Tokens.Type.Heading1Size, Tokens.Type.Heading1LineHeight),
        2 => (Tokens.Type.Heading2Size, Tokens.Type.Heading2LineHeight),
        3 => (Tokens.Type.Heading3Size, Tokens.Type.Heading3LineHeight),
        4 => (Tokens.Type.Heading4Size, Tokens.Type.Heading4LineHeight),
        5 => (Tokens.Type.Heading5Size, Tokens.Type.Heading5LineHeight),
        _ => (Tokens.Type.Heading6Size, Tokens.Type.Heading6LineHeight),
    };
}
