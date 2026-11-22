using System.Globalization;
using System.Text;

namespace NeoChat.Windows.Core.Render;

/// <summary>
/// §2 block parser: a line scanner that never throws. Malformed, unterminated or oversized input always
/// produces a model — an open fence becomes <c>code{closed:false}</c>, an open <c>$$</c> becomes
/// <c>math{closed:false}</c>, and unrecognised constructs become <c>unknown</c> (§4.1).
/// <para>
/// The scanner is deterministic and prefix-monotone: extending the source can only grow or append to the
/// last block, never rewrite an earlier one. That is what makes §4.5 hold for
/// <see cref="IncrementalRenderParser"/>.
/// </para>
/// </summary>
internal sealed class MarkdownBlockParser
{
    private readonly string[] _lines;
    private readonly int[] _lineOffsets;
    private readonly int _depth;

    private MarkdownBlockParser(string[] lines, int[] lineOffsets, int depth)
    {
        _lines = lines;
        _lineOffsets = lineOffsets;
        _depth = depth;
    }

    internal static List<Block> Parse(string markdown, int depth = 0)
    {
        var lines = markdown.Split('\n');
        var offsets = new int[lines.Length];
        var offset = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            offsets[index] = offset;
            offset += lines[index].Length + 1;
        }

        return new MarkdownBlockParser(lines, offsets, depth).ParseRange(0, lines.Length);
    }

    /// <summary>Parses already-unindented container content one nesting level deeper.</summary>
    internal static List<Block> ParseNested(List<string> lines, int depth) =>
        lines.Count == 0 ? [] : Parse(string.Join('\n', lines), depth);

    private List<Block> ParseRange(int start, int end)
    {
        var blocks = new List<Block>();
        var index = start;

        while (index < end && blocks.Count < RenderLimits.MaxBlocks)
        {
            if (IsBlank(_lines[index]))
            {
                index++;
                continue;
            }

            var startLine = index;
            var before = blocks.Count;

            if (TryFence(index, end, out var fence, out var fenceNext))
            {
                blocks.Add(fence);
                index = fenceNext;
            }
            else if (TryMathBlock(index, end, out var math, out var mathNext))
            {
                blocks.Add(math);
                index = mathNext;
            }
            else if (TryAtxHeading(_lines[index], out var level, out var headingText))
            {
                blocks.Add(new HeadingBlock { Level = level, Inlines = MarkdownInlineParser.Parse(headingText) });
                index++;
            }
            else if (TryHr(_lines[index]))
            {
                blocks.Add(new HrBlock());
                index++;
            }
            else if (TryFootnoteDefinition(index, end, out var footnote, out var footnoteNext))
            {
                blocks.Add(footnote);
                index = footnoteNext;
            }
            else if (TryLinkDefinition(_lines[index], out var definition))
            {
                blocks.Add(definition);
                index++;
            }
            else if (IsQuoteStart(_lines[index]))
            {
                index = ParseQuote(index, end, blocks);
            }
            else if (TryListStart(index, end, out var list, out var listNext))
            {
                blocks.Add(list);
                index = listNext;
            }
            else if (TryHtmlBlock(index, end, out var html, out var htmlNext))
            {
                blocks.Add(html);
                index = htmlNext;
            }
            else if (TryTable(index, end, out var table, out var tableNext))
            {
                blocks.Add(table);
                index = tableNext;
            }
            else if (IsIndentedCode(_lines[index]))
            {
                index = ParseIndentedCode(index, end, blocks);
            }
            else
            {
                index = ParseParagraph(index, end, blocks);
            }

            if (blocks.Count == before)
            {
                // Defensive: a branch that emits nothing must still advance, or the loop spins.
                index = Math.Max(index, startLine + 1);
                continue;
            }

            for (var emitted = before; emitted < blocks.Count; emitted++)
            {
                blocks[emitted].SourceStart = _lineOffsets[startLine];
            }
        }

        return blocks;
    }

    private static bool IsBlank(string line) => line.AsSpan().Trim().Length == 0;

    private static int IndentOf(string line)
    {
        var indent = 0;
        foreach (var ch in line)
        {
            if (ch == ' ')
            {
                indent++;
            }
            else if (ch == '\t')
            {
                indent += 4 - (indent % 4);
            }
            else
            {
                break;
            }
        }

        return indent;
    }

    private static bool IsIndentedCode(string line) => IndentOf(line) >= 4 && !IsBlank(line);

    private static string StripIndent(string line, int count)
    {
        var stripped = 0;
        var index = 0;
        while (index < line.Length && stripped < count)
        {
            if (line[index] == ' ')
            {
                stripped++;
                index++;
            }
            else if (line[index] == '\t')
            {
                stripped += 4 - (stripped % 4);
                index++;
            }
            else
            {
                break;
            }
        }

        return line[index..];
    }

    // ---- 围栏代码块 / mermaid（§2.1 info string） -----------------------------------------

    private bool TryFence(int index, int end, out Block block, out int next)
    {
        block = null!;
        next = index;

        var line = _lines[index];
        if (IndentOf(line) > 3)
        {
            return false;
        }

        var body = line.TrimStart(' ');
        if (body.Length < 3 || body[0] is not ('`' or '~'))
        {
            return false;
        }

        var fenceChar = body[0];
        var fenceLength = 0;
        while (fenceLength < body.Length && body[fenceLength] == fenceChar)
        {
            fenceLength++;
        }

        if (fenceLength < 3)
        {
            return false;
        }

        var info = body[fenceLength..].Trim();
        if (fenceChar == '`' && info.Contains('`', StringComparison.Ordinal))
        {
            return false;
        }

        var content = new List<string>();
        var closed = false;
        var cursor = index + 1;
        for (; cursor < end; cursor++)
        {
            if (IsClosingFence(_lines[cursor], fenceChar, fenceLength))
            {
                closed = true;
                cursor++;
                break;
            }

            if (content.Count < RenderLimits.MaxCodeLines)
            {
                content.Add(_lines[cursor]);
            }
        }

        var (lang, meta) = FenceInfo.Parse(info);
        var text = string.Join('\n', content);
        block = lang is not null && lang.Equals("mermaid", StringComparison.OrdinalIgnoreCase)
            ? new MermaidBlock { Src = text, Closed = closed, Kind = MermaidKinds.Classify(text) }
            : new CodeBlock { Lang = lang, Text = text, Closed = closed, Meta = meta };

        next = cursor;
        return true;
    }

    private static bool IsClosingFence(string line, char fenceChar, int minLength)
    {
        if (IndentOf(line) > 3)
        {
            return false;
        }

        var body = line.TrimStart(' ');
        var run = 0;
        while (run < body.Length && body[run] == fenceChar)
        {
            run++;
        }

        return run >= minLength && body[run..].Trim().Length == 0;
    }

    // ---- 块级公式 -------------------------------------------------------------------------

    private bool TryMathBlock(int index, int end, out Block block, out int next)
    {
        block = null!;
        next = index;

        var line = _lines[index];
        if (IndentOf(line) > 3)
        {
            return false;
        }

        var body = line.TrimStart(' ');
        string open;
        string close;
        if (body.StartsWith("$$", StringComparison.Ordinal))
        {
            open = "$$";
            close = "$$";
        }
        else if (body.StartsWith("\\[", StringComparison.Ordinal))
        {
            open = "\\[";
            close = "\\]";
        }
        else
        {
            return false;
        }

        var afterOpen = body[open.Length..];
        var inlineClose = afterOpen.IndexOf(close, StringComparison.Ordinal);
        if (inlineClose >= 0)
        {
            block = new MathBlock { Tex = afterOpen[..inlineClose].Trim(), Closed = true };
            next = index + 1;
            return true;
        }

        var buffer = new StringBuilder();
        if (afterOpen.Trim().Length > 0)
        {
            buffer.Append(afterOpen.Trim()).Append('\n');
        }

        var closed = false;
        var cursor = index + 1;
        for (; cursor < end; cursor++)
        {
            var contentLine = _lines[cursor];
            var closeAt = contentLine.IndexOf(close, StringComparison.Ordinal);
            if (closeAt >= 0)
            {
                var tail = contentLine[..closeAt].Trim();
                if (tail.Length > 0)
                {
                    buffer.Append(tail).Append('\n');
                }

                closed = true;
                cursor++;
                break;
            }

            buffer.Append(contentLine).Append('\n');
        }

        block = new MathBlock { Tex = buffer.ToString().TrimEnd('\n'), Closed = closed };
        next = cursor;
        return true;
    }

    // ---- ATX 标题 / 分隔线 ------------------------------------------------------------------

    private static bool TryAtxHeading(string line, out int level, out string text)
    {
        level = 0;
        text = string.Empty;

        if (IndentOf(line) > 3)
        {
            return false;
        }

        var body = line.TrimStart(' ');
        var hashes = 0;
        while (hashes < body.Length && body[hashes] == '#')
        {
            hashes++;
        }

        if (hashes is 0 or > 6)
        {
            return false;
        }

        if (hashes < body.Length && body[hashes] != ' ')
        {
            return false;
        }

        level = hashes;
        var rest = hashes < body.Length ? body[(hashes + 1)..].Trim() : string.Empty;
        text = rest.TrimEnd('#').TrimEnd();
        return true;
    }

    private static bool TryHr(string line)
    {
        if (IndentOf(line) > 3)
        {
            return false;
        }

        var body = line.Trim();
        if (body.Length < 3)
        {
            return false;
        }

        var marker = body[0];
        if (marker is not ('-' or '*' or '_'))
        {
            return false;
        }

        var count = 0;
        foreach (var ch in body)
        {
            if (ch == marker)
            {
                count++;
            }
            else if (ch != ' ')
            {
                return false;
            }
        }

        return count >= 3;
    }

    private static bool TrySetextUnderline(string line, out int level)
    {
        level = 0;
        if (IndentOf(line) > 3)
        {
            return false;
        }

        var body = line.Trim();
        if (body.Length == 0)
        {
            return false;
        }

        var marker = body[0];
        if (marker is not ('=' or '-'))
        {
            return false;
        }

        foreach (var ch in body)
        {
            if (ch != marker)
            {
                return false;
            }
        }

        level = marker == '=' ? 1 : 2;
        return true;
    }

    // ---- 脚注定义 / 链接定义 ----------------------------------------------------------------

    private bool TryFootnoteDefinition(int index, int end, out Block block, out int next)
    {
        block = null!;
        next = index;

        var line = _lines[index];
        if (IndentOf(line) > 3)
        {
            return false;
        }

        var body = line.TrimStart(' ');
        if (!body.StartsWith("[^", StringComparison.Ordinal))
        {
            return false;
        }

        var labelEnd = body.IndexOf(']', StringComparison.Ordinal);
        if (labelEnd < 3 || labelEnd + 1 >= body.Length || body[labelEnd + 1] != ':')
        {
            return false;
        }

        var label = body[2..labelEnd];
        var first = body[(labelEnd + 2)..].Trim();
        var content = new List<string>();
        if (first.Length > 0)
        {
            content.Add(first);
        }

        var cursor = index + 1;
        while (cursor < end)
        {
            var current = _lines[cursor];
            if (IsBlank(current))
            {
                var lookahead = cursor + 1;
                while (lookahead < end && IsBlank(_lines[lookahead]))
                {
                    lookahead++;
                }

                if (lookahead < end && IndentOf(_lines[lookahead]) >= 2)
                {
                    content.Add(string.Empty);
                    cursor++;
                    continue;
                }

                break;
            }

            if (IndentOf(current) >= 2)
            {
                content.Add(StripIndent(current, 2));
                cursor++;
                continue;
            }

            break;
        }

        block = new FootnoteDefBlock { Label = label, Blocks = ParseNested(content, _depth + 1) };
        next = cursor;
        return true;
    }

    private static bool TryLinkDefinition(string line, out Block block)
    {
        block = null!;
        if (IndentOf(line) > 3)
        {
            return false;
        }

        var body = line.TrimStart(' ');
        if (body.Length < 4 || body[0] != '[' || body[1] == '^')
        {
            return false;
        }

        var labelEnd = body.IndexOf(']', StringComparison.Ordinal);
        if (labelEnd < 1 || labelEnd + 1 >= body.Length || body[labelEnd + 1] != ':')
        {
            return false;
        }

        if (body[(labelEnd + 2)..].Trim().Length == 0)
        {
            return false;
        }

        block = new UnknownBlock { Kind = "link_definition", Raw = body };
        return true;
    }

    // ---- 引用块 ---------------------------------------------------------------------------

    private static bool IsQuoteStart(string line) =>
        IndentOf(line) <= 3 && line.TrimStart(' ').StartsWith('>');

    private int ParseQuote(int index, int end, List<Block> sink)
    {
        var content = new List<string>();
        var cursor = index;
        while (cursor < end)
        {
            var line = _lines[cursor];
            if (IsQuoteStart(line))
            {
                var body = line.TrimStart(' ');
                body = body[1..];
                if (body.StartsWith(' '))
                {
                    body = body[1..];
                }

                content.Add(body);
                cursor++;
                continue;
            }

            if (IsBlank(line))
            {
                var lookahead = cursor + 1;
                while (lookahead < end && IsBlank(_lines[lookahead]))
                {
                    lookahead++;
                }

                if (lookahead < end && IsQuoteStart(_lines[lookahead]))
                {
                    content.Add(string.Empty);
                    cursor++;
                    continue;
                }

                break;
            }

            if (StartsNewBlock(line))
            {
                break;
            }

            // Lazy continuation: a plain line extends the quote's last paragraph.
            content.Add(line);
            cursor++;
        }

        if (_depth >= RenderLimits.MaxContainerDepth)
        {
            sink.Add(new UnknownBlock { Kind = "quote", Raw = string.Join('\n', content) });
            return cursor;
        }

        sink.Add(new QuoteBlock
        {
            Blocks = ParseNested(content, _depth + 1),
            Depth = _depth + 1,
        });
        return cursor;
    }

    // ---- 列表 -----------------------------------------------------------------------------

    private readonly record struct ListMarker(bool Ordered, int Number, int Indent, int ContentIndent, string Content);

    private static bool TryParseMarker(string line, out ListMarker marker)
    {
        marker = default;
        var indent = IndentOf(line);
        if (indent > 3)
        {
            return false;
        }

        var body = line.TrimStart(' ');
        if (body.Length == 0)
        {
            return false;
        }

        var cursor = 0;
        var ordered = false;
        var number = 1;

        if (body[0] is '-' or '+' or '*')
        {
            cursor = 1;
        }
        else if (char.IsAsciiDigit(body[0]))
        {
            var digits = 0;
            while (cursor < body.Length && char.IsAsciiDigit(body[cursor]) && digits < 9)
            {
                digits++;
                cursor++;
            }

            if (digits == 0 || cursor >= body.Length || body[cursor] is not ('.' or ')'))
            {
                return false;
            }

            ordered = true;
            number = int.Parse(body[..cursor], CultureInfo.InvariantCulture);
            cursor++;
        }
        else
        {
            return false;
        }

        if (cursor < body.Length && body[cursor] != ' ')
        {
            return false;
        }

        var beforeSpaces = cursor;
        while (cursor < body.Length && body[cursor] == ' ' && cursor - beforeSpaces < 4)
        {
            cursor++;
        }

        marker = new ListMarker(ordered, number, indent, indent + cursor, body[cursor..]);
        return true;
    }

    private bool TryListStart(int index, int end, out Block block, out int next)
    {
        block = null!;
        next = index;

        if (!TryParseMarker(_lines[index], out var first))
        {
            return false;
        }

        if (_depth >= RenderLimits.MaxContainerDepth)
        {
            var raw = new StringBuilder(_lines[index]);
            var scan = index + 1;
            while (scan < end && !IsBlank(_lines[scan]))
            {
                raw.Append('\n').Append(_lines[scan]);
                scan++;
            }

            block = new UnknownBlock { Kind = "list", Raw = raw.ToString() };
            next = scan;
            return true;
        }

        var items = new List<ListItem>();
        var loose = false;
        var cursor = index;
        var ordered = first.Ordered;
        var start = first.Number;

        while (cursor < end && items.Count < RenderLimits.MaxBlocks)
        {
            if (!TryParseMarker(_lines[cursor], out var marker) || marker.Ordered != ordered)
            {
                break;
            }

            var contentIndent = marker.ContentIndent;
            var itemLines = new List<string> { marker.Content };
            cursor++;

            while (cursor < end)
            {
                var line = _lines[cursor];
                if (IsBlank(line))
                {
                    var lookahead = cursor + 1;
                    while (lookahead < end && IsBlank(_lines[lookahead]))
                    {
                        lookahead++;
                    }

                    if (lookahead >= end)
                    {
                        cursor = lookahead;
                        break;
                    }

                    var following = _lines[lookahead];
                    var isSibling = TryParseMarker(following, out var sibling) &&
                                    sibling.Ordered == ordered &&
                                    sibling.Indent < contentIndent;
                    if (!isSibling && IndentOf(following) < contentIndent)
                    {
                        break;
                    }

                    loose = true;
                    for (var blank = cursor; blank < lookahead; blank++)
                    {
                        itemLines.Add(string.Empty);
                    }

                    cursor = lookahead;
                    if (isSibling)
                    {
                        break;
                    }

                    continue;
                }

                if (TryParseMarker(line, out var nestedOrSibling) &&
                    nestedOrSibling.Ordered == ordered &&
                    nestedOrSibling.Indent < contentIndent)
                {
                    break;
                }

                if (IndentOf(line) >= contentIndent)
                {
                    itemLines.Add(StripIndent(line, contentIndent));
                    cursor++;
                    continue;
                }

                if (StartsNewBlock(line))
                {
                    break;
                }

                itemLines.Add(line.Trim());
                cursor++;
            }

            var (checkedState, content) = ExtractTaskMarker(itemLines);
            items.Add(new ListItem { Checked = checkedState, Blocks = ParseNested(content, _depth + 1) });
        }

        block = new ListBlock
        {
            Ordered = ordered,
            Start = start,
            Tight = !loose,
            Items = items,
            Depth = _depth + 1,
        };
        next = cursor;
        return true;
    }

    private static (bool? Checked, List<string> Lines) ExtractTaskMarker(List<string> lines)
    {
        if (lines.Count == 0)
        {
            return (null, lines);
        }

        var first = lines[0];
        if (first.Length < 3 || first[0] != '[' || first[2] != ']')
        {
            return (null, lines);
        }

        bool? checkedState = first[1] switch
        {
            ' ' => false,
            'x' or 'X' => true,
            _ => null,
        };

        if (checkedState is null || (first.Length > 3 && first[3] != ' '))
        {
            return (null, lines);
        }

        var updated = new List<string>(lines);
        updated[0] = first.Length > 3 ? first[4..] : string.Empty;
        return (checkedState, updated);
    }

    // ---- 原始 HTML / 缩进代码 ---------------------------------------------------------------

    private bool TryHtmlBlock(int index, int end, out Block block, out int next)
    {
        block = null!;
        next = index;

        var line = _lines[index];
        if (IndentOf(line) > 3)
        {
            return false;
        }

        var body = line.TrimStart(' ');
        if (body.Length < 2 || body[0] != '<' || !(char.IsLetter(body[1]) || body[1] is '/' or '!' or '?'))
        {
            return false;
        }

        var buffer = new List<string>();
        var cursor = index;
        while (cursor < end && !IsBlank(_lines[cursor]))
        {
            buffer.Add(_lines[cursor]);
            cursor++;
        }

        block = new HtmlBlock { Source = string.Join('\n', buffer) };
        next = cursor;
        return true;
    }

    private int ParseIndentedCode(int index, int end, List<Block> sink)
    {
        var buffer = new List<string>();
        var cursor = index;
        while (cursor < end)
        {
            var line = _lines[cursor];
            if (IsBlank(line))
            {
                var lookahead = cursor + 1;
                while (lookahead < end && IsBlank(_lines[lookahead]))
                {
                    lookahead++;
                }

                if (lookahead < end && IsIndentedCode(_lines[lookahead]))
                {
                    buffer.Add(string.Empty);
                    cursor++;
                    continue;
                }

                break;
            }

            if (!IsIndentedCode(line))
            {
                break;
            }

            buffer.Add(StripIndent(line, 4));
            cursor++;
        }

        sink.Add(new CodeBlock
        {
            Lang = null,
            Text = string.Join('\n', buffer),
            Closed = true,
            Meta = new CodeMeta(),
        });
        return cursor;
    }

    // ---- 表格 -----------------------------------------------------------------------------

    private bool TryTable(int index, int end, out Block block, out int next)
    {
        block = null!;
        next = index;

        if (index + 1 >= end || !HasUnescapedPipe(_lines[index]))
        {
            return false;
        }

        var headerCells = SplitTableCells(_lines[index]);
        var align = ParseDelimiterRow(_lines[index + 1]);
        if (align is null || align.Count != headerCells.Count)
        {
            return false;
        }

        var columns = Math.Min(align.Count, RenderLimits.MaxTableColumns);
        var header = new List<IReadOnlyList<Inline>>(columns);
        for (var column = 0; column < columns; column++)
        {
            header.Add(MarkdownInlineParser.ParseCell(headerCells[column]));
        }

        var rows = new List<IReadOnlyList<IReadOnlyList<Inline>>>();
        var cursor = index + 2;
        while (cursor < end && rows.Count < RenderLimits.MaxTableRows)
        {
            var line = _lines[cursor];
            if (IsBlank(line) || !HasUnescapedPipe(line))
            {
                break;
            }

            var cells = SplitTableCells(line);
            var row = new List<IReadOnlyList<Inline>>(columns);
            for (var column = 0; column < columns; column++)
            {
                row.Add(MarkdownInlineParser.ParseCell(column < cells.Count ? cells[column] : string.Empty));
            }

            rows.Add(row);
            cursor++;
        }

        block = new TableBlock
        {
            Align = align.Take(columns).ToArray(),
            Header = header,
            Rows = rows,
        };
        next = cursor;
        return true;
    }

    private static bool HasUnescapedPipe(string line)
    {
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] == '\\')
            {
                index++;
                continue;
            }

            if (line[index] == '|')
            {
                return true;
            }
        }

        return false;
    }

    private static List<string?>? ParseDelimiterRow(string line)
    {
        if (!HasUnescapedPipe(line))
        {
            return null;
        }

        var cells = SplitTableCells(line);
        var align = new List<string?>(cells.Count);
        foreach (var cell in cells)
        {
            var value = cell.Trim();
            if (value.Length == 0)
            {
                return null;
            }

            var left = value[0] == ':';
            var right = value[^1] == ':';
            var dashes = value.Trim(':');
            if (dashes.Length == 0 || !dashes.All(static ch => ch == '-'))
            {
                return null;
            }

            align.Add(left && right ? TableAlign.Center : right ? TableAlign.Right : left ? TableAlign.Left : null);
        }

        return align;
    }

    /// <summary>GFM cell split: <c>\|</c> never splits (and is unfolded to a literal pipe), code spans are honoured.</summary>
    private static List<string> SplitTableCells(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|'))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.EndsWith('|') && !trimmed.EndsWith("\\|", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^1];
        }

        var cells = new List<string>();
        var buffer = new StringBuilder();
        var inCode = false;
        for (var index = 0; index < trimmed.Length; index++)
        {
            var ch = trimmed[index];
            if (ch == '\\' && index + 1 < trimmed.Length)
            {
                var next = trimmed[index + 1];
                buffer.Append(next == '|' ? '|' : ch);
                if (next != '|')
                {
                    buffer.Append(next);
                }

                index++;
                continue;
            }

            if (ch == '`')
            {
                inCode = !inCode;
                buffer.Append(ch);
                continue;
            }

            if (ch == '|' && !inCode)
            {
                cells.Add(buffer.ToString().Trim());
                buffer.Clear();
                continue;
            }

            buffer.Append(ch);
        }

        cells.Add(buffer.ToString().Trim());
        return cells;
    }

    // ---- 段落 -----------------------------------------------------------------------------

    private int ParseParagraph(int index, int end, List<Block> sink)
    {
        // Leading indent is structural (≤3 spaces); trailing spaces are kept because two of them are a hard break.
        var lines = new List<string> { _lines[index].TrimStart(' ') };
        var cursor = index + 1;
        var headingLevel = 0;

        while (cursor < end)
        {
            var line = _lines[cursor];
            if (IsBlank(line))
            {
                break;
            }

            if (TrySetextUnderline(line, out var level))
            {
                headingLevel = level;
                cursor++;
                break;
            }

            if (StartsNewBlock(line))
            {
                break;
            }

            lines.Add(line.TrimStart(' '));
            cursor++;
        }

        var inlines = MarkdownInlineParser.Parse(string.Join('\n', lines));

        if (headingLevel > 0)
        {
            sink.Add(new HeadingBlock { Level = headingLevel, Inlines = inlines });
            return cursor;
        }

        if (inlines.Count == 1 && inlines[0] is ImageInline image && lines.Count == 1)
        {
            // §2.2: a paragraph holding exactly one image and nothing else is an image block.
            sink.Add(new ImageBlock { Src = image.Src, Alt = image.Alt, Title = image.Title });
            return cursor;
        }

        sink.Add(new ParagraphBlock { Inlines = inlines });
        return cursor;
    }

    /// <summary>Does this line begin a construct that interrupts a paragraph (CommonMark block starts)?</summary>
    private static bool StartsNewBlock(string line)
    {
        if (IsBlank(line))
        {
            return true;
        }

        if (TryAtxHeading(line, out _, out _) || TryHr(line) || IsQuoteStart(line))
        {
            return true;
        }

        if (IndentOf(line) > 3)
        {
            return false;
        }

        var body = line.TrimStart(' ');
        if (body.Length >= 3 &&
            (body.StartsWith("```", StringComparison.Ordinal) || body.StartsWith("~~~", StringComparison.Ordinal)))
        {
            return true;
        }

        if (body.StartsWith("$$", StringComparison.Ordinal) || body.StartsWith("\\[", StringComparison.Ordinal))
        {
            return true;
        }

        if (body.Length >= 2 && body[0] == '<' && (char.IsLetter(body[1]) || body[1] is '/' or '!' or '?'))
        {
            return true;
        }

        if (body.Length >= 3 && body[0] == '[' && body[1] == '^')
        {
            return true;
        }

        return TryParseMarker(line, out var marker) && marker.Content.Length > 0;
    }
}

/// <summary>§2.1 info string: <c>lang (SP attr)*</c>; unrecognised attributes are ignored, never an error.</summary>
internal static class FenceInfo
{
    internal static (string? Lang, CodeMeta Meta) Parse(string info)
    {
        if (string.IsNullOrWhiteSpace(info))
        {
            return (null, new CodeMeta());
        }

        var tokens = Tokenize(info);
        if (tokens.Count == 0)
        {
            return (null, new CodeMeta());
        }

        var lang = tokens[0];
        string? fileName = null;
        var lineNumbers = false;
        var recognized = true;

        for (var index = 1; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token is "ln" or "line_numbers")
            {
                lineNumbers = true;
                continue;
            }

            if (token.StartsWith("filename=", StringComparison.Ordinal) && token.Length > "filename=".Length)
            {
                fileName = token["filename=".Length..];
                continue;
            }

            recognized = false;
        }

        if (!recognized)
        {
            // §2.1: an info string we cannot parse is kept whole as the language, with empty meta.
            return (info.Trim(), new CodeMeta());
        }

        return (lang.Length == 0 ? null : lang, new CodeMeta { FileName = fileName, LineNumbers = lineNumbers });
    }

    private static List<string> Tokenize(string info)
    {
        var tokens = new List<string>();
        var buffer = new StringBuilder();
        var quoted = false;
        foreach (var ch in info)
        {
            if (ch == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (!quoted && char.IsWhiteSpace(ch))
            {
                if (buffer.Length > 0)
                {
                    tokens.Add(buffer.ToString());
                    buffer.Clear();
                }

                continue;
            }

            buffer.Append(ch);
        }

        if (buffer.Length > 0)
        {
            tokens.Add(buffer.ToString());
        }

        return tokens;
    }
}
