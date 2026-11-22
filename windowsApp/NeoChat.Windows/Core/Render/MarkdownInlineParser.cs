using System.Text;

namespace NeoChat.Windows.Core.Render;

/// <summary>
/// §3 inline parser: the closed set of 10 inline types, nesting capped at
/// <see cref="RenderLimits.MaxInlineDepth"/> (beyond that the rest of the run degrades to
/// <c>text</c> with its markers kept, so nothing is ever lost).
/// <para>
/// v1 deliberately does not resolve reference-style links (§2.3): <c>[文字][label]</c> and
/// <c>[label]</c> stay literal text, because a later definition would have to rewrite an already
/// finished block and break §4.5 prefix stability.
/// </para>
/// </summary>
internal sealed class MarkdownInlineParser
{
    private readonly string _source;
    private readonly StringBuilder _text = new();
    private readonly List<Inline> _output = [];
    private int _index;
    private int _depth;

    private MarkdownInlineParser(string source, int depth)
    {
        _source = source;
        _depth = depth;
    }

    /// <summary>Parses one block's inline content. Soft line breaks become spaces, hard ones a <c>br</c>.</summary>
    internal static List<Inline> Parse(string source) => Parse(source, 0);

    /// <summary>Parses already cell-split table text (escaped pipes are already unfolded).</summary>
    internal static List<Inline> ParseCell(string source) => Parse(source, 0);

    private static List<Inline> Parse(string source, int depth)
    {
        var parser = new MarkdownInlineParser(source, depth);
        parser.Run();
        parser.FlushText();
        return parser._output;
    }

    private void Run()
    {
        while (_index < _source.Length)
        {
            var ch = _source[_index];
            switch (ch)
            {
                case '\\':
                    HandleBackslash();
                    break;
                case '`':
                    HandleCodeSpan();
                    break;
                case '*':
                case '_':
                case '~':
                    HandleEmphasis(ch);
                    break;
                case '!' when _index + 1 < _source.Length && _source[_index + 1] == '[':
                    HandleImage();
                    break;
                case '[':
                    HandleBracket();
                    break;
                case '$':
                    HandleDollar();
                    break;
                case '\n':
                    HandleNewline();
                    break;
                default:
                    _text.Append(ch);
                    _index++;
                    break;
            }
        }
    }

    private void HandleBackslash()
    {
        // §3: \( ... \) is inline math and \[ ... \] is block math; only \( appears inline.
        if (_index + 1 < _source.Length && _source[_index + 1] == '(')
        {
            var close = _source.IndexOf("\\)", _index + 2, StringComparison.Ordinal);
            if (close >= 0)
            {
                FlushText();
                _output.Add(new MathInline { Tex = _source[(_index + 2)..close] });
                _index = close + 2;
                return;
            }
        }

        if (_index + 1 < _source.Length && IsEscapable(_source[_index + 1]))
        {
            _text.Append(_source[_index + 1]);
            _index += 2;
            return;
        }

        _text.Append('\\');
        _index++;
    }

    private static bool IsEscapable(char ch) =>
        ch is '\\' or '`' or '*' or '_' or '{' or '}' or '[' or ']' or '(' or ')' or '#' or '+' or '-' or '.' or '!' or '|' or '~' or '>' or '<' or '$' or '"' or '\'' or '^' or ':';

    private void HandleCodeSpan()
    {
        var run = CountRun(_index, '`');
        var closing = FindBacktickRun(_index + run, run);
        if (closing < 0)
        {
            _text.Append('`', run);
            _index += run;
            return;
        }

        var content = _source[(_index + run)..closing].Replace('\n', ' ');
        if (content.Length >= 2 && content[0] == ' ' && content[^1] == ' ' && content.Trim().Length > 0)
        {
            content = content[1..^1];
        }

        FlushText();
        _output.Add(new CodeInline { Text = content });
        _index = closing + run;
    }

    private int CountRun(int from, char ch)
    {
        var count = 0;
        while (from + count < _source.Length && _source[from + count] == ch)
        {
            count++;
        }

        return count;
    }

    private int FindBacktickRun(int from, int length)
    {
        for (var cursor = from; cursor < _source.Length;)
        {
            if (_source[cursor] != '`')
            {
                cursor++;
                continue;
            }

            var run = CountRun(cursor, '`');
            if (run == length)
            {
                return cursor;
            }

            cursor += run;
        }

        return -1;
    }

    /// <summary>
    /// Emphasis runs: a run of one is <c>emph</c>, two is <c>strong</c>, three nests <c>strong</c> around
    /// <c>emph</c> (so <c>***x***</c> is not "<c>**</c> then a stray star"). Runs that find no closer stay
    /// literal text.
    /// </summary>
    private void HandleEmphasis(char marker)
    {
        var run = CountRun(_index, marker);
        if (marker == '~')
        {
            if (run < 2)
            {
                _text.Append('~');
                _index++;
                return;
            }

            HandleDelimited("~~", static inlines => new StrikeInline { Inlines = inlines });
            return;
        }

        for (var use = Math.Min(run, 3); use >= 1; use--)
        {
            if (!CanOpenDelimiter(marker, _index, use))
            {
                continue;
            }

            var closer = new string(marker, use);
            var close = FindClosing(closer, _index + use);
            if (close < 0)
            {
                continue;
            }

            EmitEmphasis(use, close);
            return;
        }

        _text.Append(marker);
        _index++;
    }

    private void EmitEmphasis(int width, int close)
    {
        var inner = _source[(_index + width)..close];
        if (inner.Length == 0)
        {
            _text.Append(_source[_index..(_index + width)]);
            _index += width;
            return;
        }

        FlushText();
        _depth++;
        try
        {
            var inlines = _depth >= RenderLimits.MaxInlineDepth ? [new TextInline { Text = inner }] : Parse(inner, _depth);
            Inline node = width switch
            {
                1 => new EmphInline { Inlines = inlines },
                2 => new StrongInline { Inlines = inlines },
                _ => new StrongInline { Inlines = [new EmphInline { Inlines = inlines }] },
            };
            _output.Add(node);
        }
        finally
        {
            _depth--;
        }

        _index = close + width;
    }

    private bool CanOpenDelimiter(char marker, int index, int width)
    {
        var next = index + width;
        if (next >= _source.Length || char.IsWhiteSpace(_source[next]))
        {
            return false;
        }

        if (marker != '_')
        {
            return true;
        }

        var previousIsWord = index > 0 && IsWordChar(_source[index - 1]);
        var nextIsWord = IsWordChar(_source[next]);
        return !(previousIsWord && nextIsWord);
    }

    private void HandleDelimited(string marker, Func<IReadOnlyList<Inline>, Inline> factory)
    {
        if (_depth >= RenderLimits.MaxInlineDepth)
        {
            // §3/§5: over the nesting cap the rest of the run degrades to text with markers kept.
            _text.Append(_source[_index..]);
            _index = _source.Length;
            return;
        }

        var close = FindClosing(marker, _index + marker.Length);
        if (close < 0)
        {
            _text.Append(marker);
            _index += marker.Length;
            return;
        }

        var inner = _source[(_index + marker.Length)..close];
        if (inner.Length == 0 || inner[0] == '\n')
        {
            _text.Append(marker);
            _index += marker.Length;
            return;
        }

        FlushText();
        _depth++;
        try
        {
            _output.Add(factory(Parse(inner, _depth)));
        }
        finally
        {
            _depth--;
        }

        _index = close + marker.Length;
    }

    private int FindClosing(string marker, int from)
    {
        var cursor = from;
        while (cursor < _source.Length - marker.Length + 1)
        {
            var found = _source.IndexOf(marker, cursor, StringComparison.Ordinal);
            if (found < 0)
            {
                return -1;
            }

            var escaped = found > 0 && _source[found - 1] == '\\';
            var insideWord = marker is "*" or "_" &&
                             (found > 0 && IsWordChar(_source[found - 1])) &&
                             (found + marker.Length < _source.Length && IsWordChar(_source[found + marker.Length]));
            if (!escaped && !insideWord)
            {
                return found;
            }

            cursor = found + marker.Length;
        }

        return -1;
    }

    private static bool IsWordChar(char ch) => char.IsLetterOrDigit(ch) || ch == '_';

    private bool CanOpenSingleMarker(int index) =>
        index + 1 < _source.Length && !char.IsWhiteSpace(_source[index + 1]);

    private bool CanOpenUnderscore(int index)
    {
        if (!CanOpenSingleMarker(index))
        {
            return false;
        }

        var previousIsWord = index > 0 && IsWordChar(_source[index - 1]);
        var nextIsWord = IsWordChar(_source[index + 1]);
        return !(previousIsWord && nextIsWord);
    }

    private void HandleImage()
    {
        if (TryParseLinkTail(_index + 1, out var alt, out var dest, out var title, out var end))
        {
            FlushText();
            _output.Add(new ImageInline { Src = dest, Alt = alt, Title = title });
            _index = end;
            return;
        }

        _text.Append('!');
        _index++;
    }

    private void HandleBracket()
    {
        if (_index + 1 < _source.Length && _source[_index + 1] == '^')
        {
            var close = _source.IndexOf(']', _index + 2);
            if (close > 0)
            {
                FlushText();
                _output.Add(new FootnoteRefInline { Label = _source[(_index + 2)..close] });
                _index = close + 1;
                return;
            }
        }

        if (TryParseLinkTail(_index, out var label, out var dest2, out var title2, out var end2))
        {
            FlushText();
            _depth++;
            try
            {
                _output.Add(new LinkInline
                {
                    Dest = dest2,
                    Title = title2,
                    Inlines = _depth >= RenderLimits.MaxInlineDepth ? [new TextInline { Text = label }] : Parse(label, _depth),
                });
            }
            finally
            {
                _depth--;
            }

            _index = end2;
            return;
        }

        // §2.3: reference-style links are not resolved in v1 — keep the brackets as literal text.
        _text.Append('[');
        _index++;
    }

    /// <summary>
    /// Parses <c>[label](dest "title")</c> starting at <paramref name="start"/> (which must hold <c>[</c>).
    /// Returns false for anything that is not an inline link, leaving the cursor untouched.
    /// </summary>
    private bool TryParseLinkTail(int start, out string label, out string dest, out string? title, out int end)
    {
        label = string.Empty;
        dest = string.Empty;
        title = null;
        end = start;

        if (start >= _source.Length || _source[start] != '[')
        {
            return false;
        }

        var labelEnd = FindClosingBracket(start);
        if (labelEnd < 0 || labelEnd + 1 >= _source.Length || _source[labelEnd + 1] != '(')
        {
            return false;
        }

        var cursor = labelEnd + 2;
        SkipSpaces(ref cursor);
        var destStart = cursor;
        if (cursor < _source.Length && _source[cursor] == '<')
        {
            var destEnd = _source.IndexOf('>', cursor + 1);
            if (destEnd < 0)
            {
                return false;
            }

            dest = _source[(cursor + 1)..destEnd];
            cursor = destEnd + 1;
        }
        else
        {
            var depth = 0;
            while (cursor < _source.Length)
            {
                var ch = _source[cursor];
                if (ch == '\\' && cursor + 1 < _source.Length)
                {
                    cursor += 2;
                    continue;
                }

                if (ch == '(')
                {
                    depth++;
                }
                else if (ch == ')')
                {
                    if (depth == 0)
                    {
                        break;
                    }

                    depth--;
                }
                else if (char.IsWhiteSpace(ch))
                {
                    break;
                }

                cursor++;
            }

            dest = _source[destStart..cursor];
        }

        SkipSpaces(ref cursor);
        if (cursor < _source.Length && (_source[cursor] == '"' || _source[cursor] == '\''))
        {
            var quote = _source[cursor];
            var titleEnd = _source.IndexOf(quote, cursor + 1);
            if (titleEnd < 0)
            {
                return false;
            }

            title = _source[(cursor + 1)..titleEnd];
            cursor = titleEnd + 1;
            SkipSpaces(ref cursor);
        }

        if (cursor >= _source.Length || _source[cursor] != ')')
        {
            return false;
        }

        label = _source[(start + 1)..labelEnd];
        end = cursor + 1;
        return true;
    }

    private int FindClosingBracket(int start)
    {
        var depth = 0;
        for (var cursor = start; cursor < _source.Length; cursor++)
        {
            var ch = _source[cursor];
            if (ch == '\\')
            {
                cursor++;
                continue;
            }

            if (ch == '[')
            {
                depth++;
            }
            else if (ch == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return cursor;
                }
            }
            else if (ch == '\n')
            {
                return -1;
            }
        }

        return -1;
    }

    private void SkipSpaces(ref int cursor)
    {
        while (cursor < _source.Length && (_source[cursor] == ' ' || _source[cursor] == '\t'))
        {
            cursor++;
        }
    }

    private void HandleDollar()
    {
        if (_index + 1 < _source.Length && _source[_index + 1] == '$')
        {
            _text.Append("$$");
            _index += 2;
            return;
        }

        if (_index + 1 >= _source.Length || char.IsWhiteSpace(_source[_index + 1]))
        {
            _text.Append('$');
            _index++;
            return;
        }

        for (var cursor = _index + 2; cursor < _source.Length; cursor++)
        {
            if (_source[cursor] == '\\')
            {
                cursor++;
                continue;
            }

            if (_source[cursor] != '$' || char.IsWhiteSpace(_source[cursor - 1]))
            {
                continue;
            }

            FlushText();
            _output.Add(new MathInline { Tex = _source[(_index + 1)..cursor] });
            _index = cursor + 1;
            return;
        }

        _text.Append('$');
        _index++;
    }

    private void HandleNewline()
    {
        var hardBreak = false;
        if (_text.Length >= 2 && _text[^1] == ' ' && _text[^2] == ' ')
        {
            _text.Length -= 2;
            hardBreak = true;
        }
        else if (_text.Length >= 1 && _text[^1] == '\\')
        {
            _text.Length -= 1;
            hardBreak = true;
        }

        if (hardBreak)
        {
            FlushText();
            _output.Add(new BrInline());
        }
        else if (_text.Length > 0)
        {
            _text.Append(' ');
        }

        _index++;
    }

    private void FlushText()
    {
        if (_text.Length == 0)
        {
            return;
        }

        var text = _text.ToString();
        _text.Clear();
        if (_output.Count > 0 && _output[^1] is TextInline previous)
        {
            _output[^1] = new TextInline { Text = previous.Text + text };
            return;
        }

        _output.Add(new TextInline { Text = text });
    }
}
