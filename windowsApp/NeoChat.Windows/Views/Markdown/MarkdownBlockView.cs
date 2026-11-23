using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NeoChat.Windows.Core.Design;
using NeoChat.Windows.Core.Render;
using NeoChat.Windows.Views;
using Windows.UI.Text;
using Block = NeoChat.Windows.Core.Render.Block;
using Inline = NeoChat.Windows.Core.Render.Inline;

namespace NeoChat.Windows.Views.Markdown;

/// <summary>
/// Renders one <see cref="Block"/> of the model. The view is bound to a <see cref="MarkdownBlockItem"/>
/// and updates in place while the message streams: the same element keeps the same block id, so a
/// growing paragraph or code fence never gets torn down and rebuilt (§4.3, §4.6).
/// <para>
/// Deviation for this milestone: math renders as monospaced TeX source in both the open and the closed
/// state (the layout engine is scheduled for the next milestone); the in-place switch still happens
/// because the block view is wide enough to host it later.
/// </para>
/// </summary>
internal sealed class MarkdownBlockView : ContentControl
{
    private MarkdownBlockItem? _item;
    private Block? _block;
    private InlineUIContainer? _cursorContainer;

    public MarkdownBlockView()
    {
        Padding = new Thickness(0);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        IsTabStop = false;
    }

    public void Bind(MarkdownBlockItem item)
    {
        if (!ReferenceEquals(_item, item))
        {
            if (_item is not null)
            {
                _item.PropertyChanged -= OnItemChanged;
            }

            _item = item;
            item.PropertyChanged += OnItemChanged;
            _block = null;
        }

        Render(item.Block);
    }

    public void Unbind()
    {
        DetachCursor();
        if (_item is not null)
        {
            _item.PropertyChanged -= OnItemChanged;
            _item = null;
        }

        _block = null;
        Content = null;
    }

    private void OnItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_item is not null)
        {
            Render(_item.Block);
        }
    }

    private void Render(Block block)
    {
        try
        {
            RenderCore(block);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            // §4.1 in spirit: a block that cannot be drawn degrades to a visible placeholder instead of
            // taking the whole message (and the app) down. The diagnostic goes to %TEMP% for the bug report.
            ReportRenderFailure(error, block);
            _richText = null;
            _cursorContainer = null;
            Content = new TextBlock
            {
                Text = "[" + block.GetType().Name + " 渲染失败]",
                FontFamily = MarkdownLook.Mono,
                FontSize = Tokens.Type.MonoSmallSize,
                Foreground = MarkdownLook.Danger,
                TextWrapping = TextWrapping.Wrap,
            };
        }
    }

    private static void ReportRenderFailure(Exception error, Block block)
    {
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "neochat-render-error.txt"),
                error + Environment.NewLine + "block=" + block.GetType().Name);
        }
        catch (System.IO.IOException)
        {
            // Diagnostics must never become the failure.
        }
    }

    private void RenderCore(Block block)
    {
        var sameType = _block?.GetType() == block.GetType();
        _block = block;

        switch (block)
        {
            case ParagraphBlock paragraph:
                RenderRichText(paragraph.Inlines, Tokens.Type.BodyLargeSize, Tokens.Type.BodyLargeLineHeight, FontWeights.Normal, sameType);
                break;
            case HeadingBlock heading:
                var (size, lineHeight) = MarkdownLook.HeadingScale(heading.Level);
                RenderRichText(heading.Inlines, size, lineHeight, FontWeights.SemiBold, sameType);
                break;
            case CodeBlock code:
                RenderCode(code, sameType);
                break;
            case MathBlock math:
                RenderMath(math, sameType);
                break;
            case MermaidBlock mermaid:
                RenderMermaid(mermaid, sameType);
                break;
            case TableBlock table:
                RenderTable(table, sameType);
                break;
            case ListBlock list:
                RenderList(list);
                break;
            case QuoteBlock quote:
                RenderQuote(quote);
                break;
            case HrBlock:
                Content = new Rectangle
                {
                    Height = Tokens.Border.Card,
                    Fill = MarkdownLook.Rule,
                    Margin = new Thickness(0, Tokens.Layout.HrMarginVertical, 0, Tokens.Layout.HrMarginVertical),
                };
                break;
            case HtmlBlock html:
                RenderLiteralWell(html.Source, "HTML", sameType, "按字面显示，未执行");
                break;
            case FootnoteDefBlock footnote:
                RenderFootnote(footnote);
                break;
            case ImageBlock image:
                RenderImage(image);
                break;
            case UnknownBlock unknown:
                RenderLiteralWell(unknown.Raw, unknown.Kind, sameType, "未定义语法，原样显示");
                break;
            default:
                Content = null;
                break;
        }
    }

    // ---- 段落 / 标题 -------------------------------------------------------------------

    private RichTextBlock? _richText;

    private void DetachCursor()
    {
        if (_cursorContainer?.Child is not null)
        {
            _cursorContainer.Child = null;
        }

        _cursorContainer = null;
    }

    private void RenderRichText(IReadOnlyList<Inline> inlines, double size, double lineHeight, FontWeight weight, bool sameType)
    {
        if (!sameType || _richText is null || !ReferenceEquals(Content, _richText))
        {
            _richText = new RichTextBlock
            {
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                FontFamily = MarkdownLook.Sans,
                Foreground = MarkdownLook.Ink,
            };
            Content = _richText;
        }

        _richText.FontSize = size;
        _richText.LineHeight = lineHeight;
        _richText.FontWeight = weight;

        var paragraph = new Paragraph();
        MarkdownInlineRenderer.Append(paragraph.Inlines, inlines);

        // A single caret element is re-parented as the tail block changes. XAML rejects a child that still
        // has a parent (E_INVALIDARG), so the previous container is emptied before the caret moves.
        DetachCursor();
        if (_item?.Cursor is { } cursor)
        {
            _cursorContainer = new InlineUIContainer { Child = cursor };
            paragraph.Inlines.Add(_cursorContainer);
        }

        _richText.Blocks.Clear();
        _richText.Blocks.Add(paragraph);
    }

    // ---- 代码井（§1.3 M13–M16、§7 圆角与内边距） --------------------------------------

    private TextBlock? _codeText;
    private TextBlock? _codeLabel;
    private FontIcon? _codeCopyGlyph;

    private void RenderCode(CodeBlock code, bool sameType)
    {
        if (!sameType || _codeText is null || _codeLabel is null)
        {
            _codeLabel = new TextBlock
            {
                FontFamily = MarkdownLook.Mono,
                FontSize = Tokens.Type.MonoLabelSize,
                LineHeight = Tokens.Type.MonoLabelLineHeight,
                Foreground = MarkdownLook.InkMuted,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            _codeText = new TextBlock
            {
                FontFamily = MarkdownLook.Mono,
                FontSize = Tokens.Type.MonoSize,
                LineHeight = Tokens.Type.MonoLineHeight,
                Foreground = MarkdownLook.CodeWellFg,
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.NoWrap,
            };

            // 代码块右上角的独立复制键：只复制这一块，不碰整条消息。
            _codeCopyGlyph = new FontIcon
            {
                FontFamily = MarkdownLook.IconFont,
                FontSize = Tokens.Layout.IconGlyphSize,
                Glyph = CopyGlyph,
            };
            var copyKey = new Button
            {
                Width = Tokens.Layout.CodeCopyKeySize,
                Height = Tokens.Layout.CodeCopyKeySize,
                Padding = new Thickness(0),
                Background = MarkdownLook.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(Tokens.Radius.Xs),
                Content = _codeCopyGlyph,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(copyKey, "复制代码");
            ToolTipService.SetToolTip(copyKey, "复制代码");
            copyKey.Click += OnCopyCodeClick;

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.Children.Add(_codeLabel);
            Grid.SetColumn(copyKey, 1);
            headerGrid.Children.Add(copyKey);

            var header = new Border
            {
                Background = MarkdownLook.CodeWellHeader,
                Padding = new Thickness(Tokens.Layout.CodeWellPaddingStart, Tokens.Space.Xs, Tokens.Layout.CodeWellPaddingEnd, Tokens.Space.Xs),
                Child = headerGrid,
            };

            var body = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollMode = ScrollMode.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollMode = ScrollMode.Disabled,
                Padding = new Thickness(
                    Tokens.Layout.CodeWellPaddingStart,
                    Tokens.Layout.CodeWellPaddingTop,
                    Tokens.Layout.CodeWellPaddingEnd,
                    Tokens.Layout.CodeWellPaddingBottom),
                Content = _codeText,
            };

            var stack = new StackPanel();
            stack.Children.Add(header);
            stack.Children.Add(body);

            Content = new Border
            {
                Background = MarkdownLook.CodeWellBg,
                BorderBrush = MarkdownLook.CodeWellBorder,
                BorderThickness = new Thickness(Tokens.Border.Card),
                CornerRadius = new CornerRadius(Tokens.Layout.CodeWellRadius),
                Child = stack,
            };
        }

        _codeLabel!.Text = CodeLabel(code);
        _codeText!.Text = code.Text;
    }

    private static string CodeLabel(CodeBlock code)
    {
        var language = string.IsNullOrEmpty(code.Lang) ? "text" : code.Lang!;
        var name = code.Meta.FileName;
        return name is null ? language : language + "  " + name;
    }

    // ---- 公式（本期等宽降级） -----------------------------------------------------------

    private TextBlock? _mathText;
    private TextBlock? _mathBadge;

    private void RenderMath(MathBlock math, bool sameType)
    {
        if (!sameType || _mathText is null || _mathBadge is null)
        {
            _mathBadge = new TextBlock
            {
                FontFamily = MarkdownLook.Sans,
                FontSize = Tokens.Layout.MathBadgeSize,
                Foreground = MarkdownLook.InkFaint,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _mathText = new TextBlock
            {
                FontFamily = MarkdownLook.Mono,
                FontSize = Tokens.Type.MonoSize,
                LineHeight = Tokens.Type.MonoLineHeight,
                Foreground = MarkdownLook.CodeWellFg,
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap,
            };

            var stack = new StackPanel { Spacing = Tokens.Space.Xs };
            stack.Children.Add(_mathBadge);
            stack.Children.Add(_mathText);

            Content = new Border
            {
                Background = MarkdownLook.CodeWellBg,
                BorderBrush = MarkdownLook.CodeWellBorder,
                BorderThickness = new Thickness(Tokens.Border.Card),
                CornerRadius = new CornerRadius(Tokens.Layout.CodeWellRadius),
                Padding = new Thickness(
                    Tokens.Layout.CodeWellPaddingStart,
                    Tokens.Layout.CodeWellPaddingTop,
                    Tokens.Layout.CodeWellPaddingEnd,
                    Tokens.Layout.CodeWellPaddingBottom),
                Child = stack,
            };
        }

        _mathBadge!.Text = math.Closed ? "TeX 公式（排版下期）" : "TeX 公式 · 未闭合（排版下期）";
        _mathText!.Text = math.Tex;
    }

    private void RenderMermaid(MermaidBlock mermaid, bool sameType)
    {
        var note = mermaid.Kind == MermaidKinds.Other
            ? "Mermaid " + mermaid.Kind + "（本期按源码显示）"
            : "Mermaid " + mermaid.Kind + "（图表渲染下期）";
        RenderLiteralWell(mermaid.Src, note, sameType, mermaid.Closed ? string.Empty : "未闭合");
    }

    // ---- 表格 ---------------------------------------------------------------------------

    private int _tableShape;

    private void RenderTable(TableBlock table, bool sameType)
    {
        var columns = table.Header.Count;
        if (sameType && _tableShape == HashCode.Combine(columns, table.Rows.Count) && Content is Grid reused)
        {
            // Same shape: rewrite cell text only, so the table never flickers while rows stream in.
            FillTable(reused, table, columns);
            return;
        }

        _tableShape = HashCode.Combine(columns, table.Rows.Count);

        var grid = new Grid
        {
            BorderBrush = MarkdownLook.Rule,
            BorderThickness = new Thickness(Tokens.Border.Hairline),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        for (var column = 0; column < columns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        }

        for (var row = 0; row <= table.Rows.Count; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        FillTable(grid, table, columns);

        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
            Content = grid,
        };
    }

    private static void FillTable(Grid grid, TableBlock table, int columns)
    {
        grid.Children.Clear();
        for (var column = 0; column < columns; column++)
        {
            grid.Children.Add(TableCell(table.Header[column], table.Align[column], isHeader: true, column, 0));
        }

        for (var row = 0; row < table.Rows.Count; row++)
        {
            var cells = table.Rows[row];
            for (var column = 0; column < columns; column++)
            {
                var cell = column < cells.Count ? cells[column] : [];
                grid.Children.Add(TableCell(cell, table.Align[column], isHeader: false, column, row + 1));
            }
        }
    }

    private static Border TableCell(IReadOnlyList<Inline> inlines, string? align, bool isHeader, int column, int row)
    {
        var text = new RichTextBlock
        {
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = MarkdownLook.Sans,
            FontSize = isHeader ? Tokens.Type.Heading5Size : Tokens.Type.BodyMediumSize,
            LineHeight = isHeader ? Tokens.Type.Heading5LineHeight : Tokens.Type.BodyMediumLineHeight,
            FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = MarkdownLook.Ink,
            TextAlignment = align switch
            {
                TableAlign.Center => TextAlignment.Center,
                TableAlign.Right => TextAlignment.Right,
                _ => TextAlignment.Left,
            },
        };

        var paragraph = new Paragraph();
        MarkdownInlineRenderer.Append(paragraph.Inlines, inlines);
        text.Blocks.Add(paragraph);

        var border = new Border
        {
            MinWidth = Tokens.Layout.TableMinColumnWidth,
            Background = isHeader ? MarkdownLook.CodeWellHeader : null,
            BorderBrush = MarkdownLook.Rule,
            BorderThickness = new Thickness(0, 0, Tokens.Border.Hairline, Tokens.Border.Hairline),
            Padding = new Thickness(
                Tokens.Layout.TableCellPaddingHorizontal,
                Tokens.Layout.TableCellPaddingVertical,
                Tokens.Layout.TableCellPaddingHorizontal,
                Tokens.Layout.TableCellPaddingVertical),
            Child = text,
        };

        Grid.SetColumn(border, column);
        Grid.SetRow(border, row);
        return border;
    }

    // ---- 列表 / 引用 / 脚注 -------------------------------------------------------------

    private void RenderList(ListBlock list)
    {
        var panel = new StackPanel { Spacing = Tokens.Space.Xxs };
        var number = list.Start;

        foreach (var item in list.Items)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Tokens.Layout.ListMarkerWidth) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var marker = new TextBlock
            {
                Text = ListMarkerText(list.Ordered, number, item.Checked),
                FontFamily = list.Ordered ? MarkdownLook.Mono : MarkdownLook.Sans,
                FontSize = Tokens.Type.MonoSize,
                LineHeight = Tokens.Type.MonoLineHeight,
                Foreground = MarkdownLook.InkMuted,
            };
            Grid.SetColumn(marker, 0);
            row.Children.Add(marker);

            var body = new StackPanel { Spacing = list.Tight ? Tokens.Space.Xxs : Tokens.Space.Sm };
            foreach (var child in item.Blocks)
            {
                body.Children.Add(NestedBlock(child));
            }

            Grid.SetColumn(body, 1);
            row.Children.Add(body);
            panel.Children.Add(row);
            number++;
        }

        Content = panel;
    }

    private static string ListMarkerText(bool ordered, int number, bool? isChecked)
    {
        var checkbox = isChecked switch
        {
            true => "\u2611 ",
            false => "\u2610 ",
            _ => string.Empty,
        };

        return ordered ? checkbox + number.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." : checkbox + "\u2022";
    }

    private void RenderQuote(QuoteBlock quote)
    {
        var body = new StackPanel { Spacing = Tokens.Space.InTurn };
        foreach (var child in quote.Blocks)
        {
            body.Children.Add(NestedBlock(child));
        }

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Tokens.Layout.BlockQuoteBarWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var bar = new Border { Background = MarkdownLook.InkGhost };
        Grid.SetColumn(bar, 0);
        row.Children.Add(bar);

        var content = new Border { Padding = new Thickness(Tokens.Space.Md, 0, 0, 0), Child = body };
        Grid.SetColumn(content, 1);
        row.Children.Add(content);

        Content = row;
    }

    private void RenderFootnote(FootnoteDefBlock footnote)
    {
        var body = new StackPanel { Spacing = Tokens.Space.Xs };
        foreach (var child in footnote.Blocks)
        {
            body.Children.Add(NestedBlock(child));
        }

        var label = new TextBlock
        {
            Text = "[" + footnote.Label + "]",
            FontFamily = MarkdownLook.Mono,
            FontSize = Tokens.Type.MonoSmallSize,
            Foreground = MarkdownLook.InkFaint,
        };

        var panel = new StackPanel { Spacing = Tokens.Space.Xxs };
        panel.Children.Add(label);
        panel.Children.Add(body);
        Content = panel;
    }

    private void RenderImage(ImageBlock image)
    {
        var stack = new StackPanel { Spacing = Tokens.Space.Xxs };
        stack.Children.Add(new TextBlock
        {
            Text = image.Alt.Length > 0 ? image.Alt : "图片",
            FontSize = Tokens.Type.BodyMediumSize,
            FontFamily = MarkdownLook.Sans,
            Foreground = MarkdownLook.InkMuted,
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(new TextBlock
        {
            Text = LinkSafety.IsAllowedImageSource(image.Src)
                ? "远程图片未加载：" + image.Src
                : "已阻止的图片地址：" + image.Src,
            FontFamily = MarkdownLook.Mono,
            FontSize = Tokens.Type.MonoSmallSize,
            Foreground = MarkdownLook.InkFaint,
            TextWrapping = TextWrapping.Wrap,
        });

        Content = new Border
        {
            Background = MarkdownLook.CodeWellBg,
            BorderBrush = MarkdownLook.CodeWellBorder,
            BorderThickness = new Thickness(Tokens.Border.Card),
            CornerRadius = new CornerRadius(Tokens.Layout.CodeWellRadius),
            Padding = new Thickness(
                Tokens.Layout.CodeWellPaddingStart,
                Tokens.Layout.CodeWellPaddingTop,
                Tokens.Layout.CodeWellPaddingEnd,
                Tokens.Layout.CodeWellPaddingBottom),
            Child = stack,
        };
    }

    private TextBlock? _literalText;
    private TextBlock? _literalBadge;

    private void RenderLiteralWell(string source, string badge, bool sameType, string note)
    {
        var label = badge.Length == 0 ? note : badge + "  " + note;
        if (!sameType || _literalText is null || _literalBadge is null)
        {
            _literalBadge = new TextBlock
            {
                FontFamily = MarkdownLook.Mono,
                FontSize = Tokens.Type.MonoLabelSize,
                LineHeight = Tokens.Type.MonoLabelLineHeight,
                Foreground = MarkdownLook.InkMuted,
            };
            _literalText = new TextBlock
            {
                FontFamily = MarkdownLook.Mono,
                FontSize = Tokens.Type.MonoSmallSize,
                LineHeight = Tokens.Type.MonoSmallLineHeight,
                Foreground = MarkdownLook.CodeWellFg,
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap,
            };

            var stack = new StackPanel { Spacing = Tokens.Space.Xxs };
            stack.Children.Add(_literalBadge);
            stack.Children.Add(_literalText);

            Content = new Border
            {
                Background = MarkdownLook.CodeWellBg,
                BorderBrush = MarkdownLook.CodeWellBorder,
                BorderThickness = new Thickness(Tokens.Border.Card),
                CornerRadius = new CornerRadius(Tokens.Layout.CodeWellRadius),
                Padding = new Thickness(
                    Tokens.Layout.CodeWellPaddingStart,
                    Tokens.Layout.CodeWellPaddingTop,
                    Tokens.Layout.CodeWellPaddingEnd,
                    Tokens.Layout.CodeWellPaddingBottom),
                Child = stack,
            };
        }

        _literalBadge!.Text = label;
        _literalText!.Text = source;
    }

    // ---- 代码块复制键 ----------------------------------------------------------------

    private const string CopyGlyph = "";
    private const string CopiedGlyph = "";

    private void OnCopyCodeClick(object sender, RoutedEventArgs e)
    {
        if (_block is not CodeBlock code || _codeCopyGlyph is null)
        {
            return;
        }

        ClipboardWriter.Write(code.Text);
        _codeCopyGlyph.Glyph = CopiedGlyph;

        // 停留时长是推断值（DESIGN-SYSTEM 只给了 A-N02 的交叉切换 160ms），见 Tokens.Layout。
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(Tokens.Layout.CopyConfirmHoldMilliseconds);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            if (_codeCopyGlyph is not null)
            {
                _codeCopyGlyph.Glyph = CopyGlyph;
            }

            timer.Stop();
        };
        timer.Start();
    }

    private static MarkdownBlockView NestedBlock(Block block)
    {
        var view = new MarkdownBlockView();
        view.Bind(new MarkdownBlockItem(block));
        return view;
    }

}
