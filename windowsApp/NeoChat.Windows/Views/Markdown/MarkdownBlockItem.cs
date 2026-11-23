using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using NeoChat.Windows.Core.Design;
using NeoChat.Windows.Core.Render;
using NeoChat.Windows.Design;

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

/// <summary>
/// Brush and text helpers shared by every block view. Colours come from the one theme palette the app
/// paints (<see cref="ThemePainter"/>), so a block drawn before a theme switch repaints with the rest of
/// the window: it holds the shared brush instance, not a copy of its colour.
/// </summary>
internal static class MarkdownLook
{
    internal static SolidColorBrush Ink => ThemePainter.Shade("Ink");

    internal static SolidColorBrush InkMuted => ThemePainter.Shade("InkMuted");

    internal static SolidColorBrush InkFaint => ThemePainter.Shade("InkFaint");

    internal static SolidColorBrush InkGhost => ThemePainter.Shade("InkGhost");

    internal static SolidColorBrush Rule => ThemePainter.Shade("RuleLight");

    internal static SolidColorBrush Seed => ThemePainter.Shade("Seed");

    internal static SolidColorBrush Danger => ThemePainter.Shade("Danger");

    internal static SolidColorBrush CodeWellBg => ThemePainter.Shade("CodeWellBg");

    internal static SolidColorBrush CodeWellHeader => ThemePainter.Shade("CodeWellHeader");

    internal static SolidColorBrush CodeWellFg => ThemePainter.Shade("CodeWellFg");

    internal static SolidColorBrush CodeWellBorder => ThemePainter.Shade("CodeWellBorder");

    internal static SolidColorBrush LineNumberFg => ThemePainter.Shade("LineNumberFg");

    internal static SolidColorBrush Transparent =>
        (SolidColorBrush)Microsoft.UI.Xaml.Application.Current.Resources["BrushTransparent"];

    internal static readonly FontFamily Sans = new(Tokens.Type.SansFamily);
    internal static readonly FontFamily Mono = new(Tokens.Type.MonoFamily);
    internal static readonly FontFamily IconFont = new(Tokens.Type.IconFamily);

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
