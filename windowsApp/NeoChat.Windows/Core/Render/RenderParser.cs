using System.Text.Json;

namespace NeoChat.Windows.Core.Render;

/// <summary>One-shot parsing: a finished message, or a test fixture.</summary>
public static class RenderMarkdown
{
    /// <summary>Parses complete Markdown into a model with <c>incomplete=false</c>. Never throws (§4.1).</summary>
    public static RenderDocument Parse(string markdown)
    {
        var text = markdown ?? string.Empty;
        var truncated = text.Length > RenderLimits.MaxInputChars;
        var blocks = MarkdownBlockParser.Parse(truncated ? text[..RenderLimits.MaxInputChars] : text);
        return RenderModelBuilder.Build(blocks, incomplete: false, truncated);
    }
}

/// <summary>
/// §4 streaming parser. Every call to <see cref="Update"/> may only extend the document: a block is
/// labelled, measured and frozen exactly once, and only the raw source of the open tail block is
/// re-read. That is §4.5 (prefix stability) by construction and §4.7 (append cost tracks the open block,
/// not the document) measured rather than hoped for.
/// </summary>
public sealed class IncrementalRenderParser
{
    /// <summary>Depth of a top-level block. §1 does not define <c>max_depth</c>; one container level counts as 1.</summary>
    private const int TopLevelDepth = 0;

    private readonly List<Block> _blocks = [];
    private string _effective = string.Empty;
    private string _tailSource = string.Empty;
    private int _tailCount;
    private bool _truncated;
    private RenderDocument? _document;

    private bool _hasMath;
    private bool _hasMermaid;
    private bool _hasTable;
    private bool _hasCode;
    private bool _hasList;
    private bool _hasFootnote;
    private int _maxDepth;

    /// <summary>Parses, or extends, the document for the full text received so far.</summary>
    public RenderDocument Update(string fullText)
    {
        ArgumentNullException.ThrowIfNull(fullText);

        var truncated = fullText.Length > RenderLimits.MaxInputChars;
        var effective = truncated ? fullText[..RenderLimits.MaxInputChars] : fullText;

        if (!effective.StartsWith(_effective, StringComparison.Ordinal))
        {
            // The caller rewrote history (a regenerated answer) — start over rather than guess.
            Reset();
        }

        if (effective.Length == _effective.Length && _document is not null && truncated == _truncated)
        {
            return _document;
        }

        var delta = effective[_effective.Length..];
        _effective = effective;
        _truncated = truncated;
        _tailSource += delta;

        // Drop the previously open tail; everything before it is frozen for good.
        if (_tailCount > 0)
        {
            _blocks.RemoveRange(_blocks.Count - _tailCount, _tailCount);
            _tailCount = 0;
        }

        var parsed = MarkdownBlockParser.Parse(_tailSource);
        if (parsed.Count > 1)
        {
            var last = parsed[^1];
            for (var index = 0; index < parsed.Count - 1; index++)
            {
                Freeze(parsed[index]);
            }

            parsed.Clear();
            parsed.Add(last);
            _tailSource = _tailSource[last.SourceStart..];
        }

        foreach (var block in parsed)
        {
            block.AssignId(_blocks.Count);
            Measure(block, TopLevelDepth);
            _blocks.Add(block);
        }

        _tailCount = parsed.Count;
        _document = new RenderDocument
        {
            Blocks = [.. _blocks],
            Flags = BuildFlags(incomplete: true),
        };
        return _document;
    }

    /// <summary>Closes the stream: the last block stops being "open" and <c>incomplete</c> turns false.</summary>
    public RenderDocument Finish()
    {
        _tailCount = 0;
        _document = new RenderDocument
        {
            Blocks = [.. _blocks],
            Flags = BuildFlags(incomplete: false),
        };
        return _document;
    }

    private void Freeze(Block block)
    {
        block.AssignId(_blocks.Count);
        Measure(block, TopLevelDepth);
        _blocks.Add(block);
    }

    private RenderFlags BuildFlags(bool incomplete) => new()
    {
        Incomplete = incomplete,
        HasMath = _hasMath,
        HasMermaid = _hasMermaid,
        HasTable = _hasTable,
        HasCode = _hasCode,
        HasList = _hasList,
        HasFootnote = _hasFootnote,
        MaxDepth = _maxDepth,
        Truncated = _truncated,
    };

    private void Measure(Block block, int depth)
    {
        RenderModelBuilder.Measure(
            block,
            depth,
            ref _hasMath,
            ref _hasMermaid,
            ref _hasTable,
            ref _hasCode,
            ref _hasList,
            ref _hasFootnote,
            ref _maxDepth);
    }

    private void Reset()
    {
        _blocks.Clear();
        _effective = string.Empty;
        _tailSource = string.Empty;
        _tailCount = 0;
        _truncated = false;
        _document = null;
        _hasMath = false;
        _hasMermaid = false;
        _hasTable = false;
        _hasCode = false;
        _hasList = false;
        _hasFootnote = false;
        _maxDepth = 0;
    }
}

/// <summary>Assigns §4.6 ids and derives §1 flags from a block list.</summary>
internal static class RenderModelBuilder
{
    /// <summary>Depth of a top-level block; see <see cref="IncrementalRenderParser"/>.</summary>
    internal const int TopLevelDepth = 0;

    internal static RenderDocument Build(IReadOnlyList<Block> source, bool incomplete, bool truncated)
    {
        var blocks = new Block[source.Count];
        for (var index = 0; index < source.Count; index++)
        {
            blocks[index] = source[index];
            blocks[index].AssignId(index);
        }

        var hasMath = false;
        var hasMermaid = false;
        var hasTable = false;
        var hasCode = false;
        var hasList = false;
        var hasFootnote = false;
        var maxDepth = 0;

        foreach (var block in blocks)
        {
            Measure(block, TopLevelDepth, ref hasMath, ref hasMermaid, ref hasTable, ref hasCode, ref hasList, ref hasFootnote, ref maxDepth);
        }

        return new RenderDocument
        {
            Blocks = blocks,
            Flags = new RenderFlags
            {
                Incomplete = incomplete,
                HasMath = hasMath,
                HasMermaid = hasMermaid,
                HasTable = hasTable,
                HasCode = hasCode,
                HasList = hasList,
                HasFootnote = hasFootnote,
                MaxDepth = maxDepth,
                Truncated = truncated,
            },
        };
    }

    internal static void Measure(
        Block block,
        int depth,
        ref bool hasMath,
        ref bool hasMermaid,
        ref bool hasTable,
        ref bool hasCode,
        ref bool hasList,
        ref bool hasFootnote,
        ref int maxDepth)
    {
        maxDepth = Math.Max(maxDepth, depth);

        switch (block)
        {
            case ParagraphBlock paragraph:
                InspectInlines(paragraph.Inlines, ref hasMath, ref hasFootnote);
                break;
            case HeadingBlock heading:
                InspectInlines(heading.Inlines, ref hasMath, ref hasFootnote);
                break;
            case CodeBlock:
                hasCode = true;
                break;
            case MathBlock:
                hasMath = true;
                break;
            case MermaidBlock:
                hasMermaid = true;
                break;
            case TableBlock table:
                hasTable = true;
                foreach (var cell in table.Header)
                {
                    InspectInlines(cell, ref hasMath, ref hasFootnote);
                }

                foreach (var row in table.Rows)
                {
                    foreach (var cell in row)
                    {
                        InspectInlines(cell, ref hasMath, ref hasFootnote);
                    }
                }

                break;
            case ListBlock list:
                hasList = true;
                foreach (var item in list.Items)
                {
                    foreach (var child in item.Blocks)
                    {
                        Measure(child, depth + 1, ref hasMath, ref hasMermaid, ref hasTable, ref hasCode, ref hasList, ref hasFootnote, ref maxDepth);
                    }
                }

                break;
            case QuoteBlock quote:
                foreach (var child in quote.Blocks)
                {
                    Measure(child, depth + 1, ref hasMath, ref hasMermaid, ref hasTable, ref hasCode, ref hasList, ref hasFootnote, ref maxDepth);
                }

                break;
            case FootnoteDefBlock footnote:
                hasFootnote = true;
                foreach (var child in footnote.Blocks)
                {
                    Measure(child, depth + 1, ref hasMath, ref hasMermaid, ref hasTable, ref hasCode, ref hasList, ref hasFootnote, ref maxDepth);
                }

                break;
        }
    }

    private static void InspectInlines(IReadOnlyList<Inline> inlines, ref bool hasMath, ref bool hasFootnote)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case MathInline:
                    hasMath = true;
                    break;
                case FootnoteRefInline:
                    hasFootnote = true;
                    break;
                case EmphInline emph:
                    InspectInlines(emph.Inlines, ref hasMath, ref hasFootnote);
                    break;
                case StrongInline strong:
                    InspectInlines(strong.Inlines, ref hasMath, ref hasFootnote);
                    break;
                case StrikeInline strike:
                    InspectInlines(strike.Inlines, ref hasMath, ref hasFootnote);
                    break;
                case LinkInline link:
                    InspectInlines(link.Inlines, ref hasMath, ref hasFootnote);
                    break;
            }
        }
    }
}

/// <summary>The §1 wire shape, used by tests and by the L1/L2 conformance manifests.</summary>
public static class RenderModelJson
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static string Serialize(RenderDocument document) => JsonSerializer.Serialize(document, Options);

    public static string SerializeBlock(Block block) => JsonSerializer.Serialize(block, Options);
}
