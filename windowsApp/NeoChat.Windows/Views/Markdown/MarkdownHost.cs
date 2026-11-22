using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using NeoChat.Windows.Core.Design;
using NeoChat.Windows.Core.Render;

namespace NeoChat.Windows.Views.Markdown;

/// <summary>
/// The assistant body: Markdown text in, a live view of <see cref="RenderDocument"/> out.
/// <para>
/// Parsing is incremental (<see cref="IncrementalRenderParser"/>), so appending one token re-reads only
/// the open block; the view list is keyed by §4.6 block id, so existing blocks keep their elements and
/// only the tail block changes. An unterminated fence or formula therefore never throws and never
/// replaces the whole body — it just grows inside the block it already belongs to.
/// </para>
/// </summary>
public sealed class MarkdownHost : UserControl
{
    private readonly IncrementalRenderParser _parser = new();
    private readonly MarkdownBlockList _items = [];
    private readonly ItemsRepeater _repeater;
    private readonly Border _trailingCursor;
    private readonly Border _inlineCursor;
    private readonly Storyboard _inlineCursorPulse;
    private readonly Storyboard _trailingCursorPulse;
    private MarkdownBlockItem? _cursorOwner;
    private string _text = string.Empty;
    private bool _showCursor;

    public MarkdownHost()
    {
        IsTabStop = false;

        _inlineCursor = CreateCursor();
        _trailingCursor = CreateCursor();
        _inlineCursorPulse = Motion.CreateCursorBlink(_inlineCursor);
        _trailingCursorPulse = Motion.CreateCursorBlink(_trailingCursor);

        _repeater = new ItemsRepeater
        {
            ItemTemplate = new MarkdownBlockFactory(),
            ItemsSource = _items,
            Layout = new StackLayout { Spacing = Tokens.Layout.MarkdownBlockGap },
        };

        _trailingCursor.Visibility = Visibility.Collapsed;

        var root = new StackPanel { Spacing = Tokens.Layout.MarkdownBlockGap };
        root.Children.Add(_repeater);
        root.Children.Add(_trailingCursor);
        Content = root;
    }

    /// <summary>The raw Markdown; append-only updates are parsed incrementally, rewrites reparse cleanly.</summary>
    public string Text
    {
        get => _text;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(next, _text, StringComparison.Ordinal))
            {
                return;
            }

            _text = next;
            Apply(_parser.Update(_text), complete: false);
        }
    }

    /// <summary>Seals the document (the message finished streaming).</summary>
    public void Complete()
    {
        Apply(_parser.Finish(), complete: true);
    }

    /// <summary>ST01/ST02 caret: shown inline at the end of running text, or below other block types.</summary>
    public bool ShowCursor
    {
        get => _showCursor;
        set
        {
            if (_showCursor == value)
            {
                return;
            }

            _showCursor = value;
            ApplyCursor();
        }
    }

    /// <summary>Latest model, exposed for diagnostics and for the render manifest.</summary>
    public RenderDocument? Document { get; private set; }

    private void Apply(RenderDocument document, bool complete)
    {
        Document = document;
        var blocks = document.Blocks;

        for (var index = 0; index < blocks.Count; index++)
        {
            if (index < _items.Count)
            {
                _items[index].Update(blocks[index]);
            }
            else
            {
                _items.Add(new MarkdownBlockItem(blocks[index]));
            }
        }

        while (_items.Count > blocks.Count)
        {
            _items.RemoveAt(_items.Count - 1);
        }

        if (complete)
        {
            _showCursor = false;
        }

        ApplyCursor();
    }

    private void ApplyCursor()
    {
        var last = _items.Count > 0 ? _items[^1] : null;
        var inlineCaret = _showCursor && last?.Block is ParagraphBlock or HeadingBlock;

        if (_cursorOwner is not null && (!inlineCaret || !ReferenceEquals(_cursorOwner, last)))
        {
            _cursorOwner.SetCursor(null);
            _cursorOwner = null;
        }

        if (inlineCaret && last is not null)
        {
            _cursorOwner = last;
            last.SetCursor(_inlineCursor);
            if (_inlineCursorPulse.GetCurrentState() != ClockState.Active)
            {
                _inlineCursorPulse.Begin();
            }
        }

        var trailingCaret = _showCursor && !inlineCaret;
        _trailingCursor.Visibility = trailingCaret ? Visibility.Visible : Visibility.Collapsed;
        if (trailingCaret && _trailingCursorPulse.GetCurrentState() != ClockState.Active)
        {
            _trailingCursorPulse.Begin();
        }
    }

    private static Border CreateCursor() => new()
    {
        Width = Tokens.Layout.CursorWidth,
        Height = Tokens.Layout.CursorHeight,
        Background = MarkdownLook.Seed,
        CornerRadius = new CornerRadius(Tokens.Radius.CursorBar),
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Left,
        Opacity = Tokens.Motion.CursorOpacityHigh,
    };
}
