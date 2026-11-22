using System.Text.Json.Serialization;

namespace NeoChat.Windows.Core.Render;

/// <summary>
/// The render AST of spec/render-model.md 1.0.0. The JSON shape produced by
/// <see cref="RenderModelJson"/> is the cross-platform contract (§1–§3): 13 block types, 10 inline
/// types, every field name spelled exactly as the spec table spells it.
/// <para>
/// Block ids (§4.6) are derived from position (<c>b0</c>, <c>b1</c>, …) rather than stored in the
/// envelope, so they are <see cref="JsonIgnoreAttribute"/>d: two parsers that agree on blocks agree on
/// ids by construction, and the wire model keeps the closed field set of §2.
/// </para>
/// </summary>
public sealed class RenderDocument
{
    [JsonPropertyName("v")]
    public int Version { get; init; } = 1;

    [JsonPropertyName("blocks")]
    public IReadOnlyList<Block> Blocks { get; init; } = [];

    [JsonPropertyName("flags")]
    public RenderFlags Flags { get; init; } = new();
}

/// <summary>§1 flags — a closed set; nothing else may be added.</summary>
public sealed class RenderFlags
{
    [JsonPropertyName("incomplete")]
    public bool Incomplete { get; init; }

    [JsonPropertyName("has_math")]
    public bool HasMath { get; init; }

    [JsonPropertyName("has_mermaid")]
    public bool HasMermaid { get; init; }

    [JsonPropertyName("has_table")]
    public bool HasTable { get; init; }

    [JsonPropertyName("has_code")]
    public bool HasCode { get; init; }

    [JsonPropertyName("has_list")]
    public bool HasList { get; init; }

    [JsonPropertyName("has_footnote")]
    public bool HasFootnote { get; init; }

    [JsonPropertyName("max_depth")]
    public int MaxDepth { get; init; }

    [JsonPropertyName("truncated")]
    public bool Truncated { get; init; }
}

/// <summary>§2 — one of the 13 block types. <see cref="Id"/> is assigned by the parser (§4.6).</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ParagraphBlock), "paragraph")]
[JsonDerivedType(typeof(HeadingBlock), "heading")]
[JsonDerivedType(typeof(CodeBlock), "code")]
[JsonDerivedType(typeof(MathBlock), "math")]
[JsonDerivedType(typeof(MermaidBlock), "mermaid")]
[JsonDerivedType(typeof(TableBlock), "table")]
[JsonDerivedType(typeof(ListBlock), "list")]
[JsonDerivedType(typeof(QuoteBlock), "quote")]
[JsonDerivedType(typeof(HrBlock), "hr")]
[JsonDerivedType(typeof(HtmlBlock), "html")]
[JsonDerivedType(typeof(FootnoteDefBlock), "footnote_def")]
[JsonDerivedType(typeof(ImageBlock), "image")]
[JsonDerivedType(typeof(UnknownBlock), "unknown")]
public abstract class Block
{
    /// <summary>Stable view key <c>b0,b1,…</c> (§4.6). Not part of the JSON envelope.</summary>
    [JsonIgnore]
    public string Id { get; internal set; } = string.Empty;

    /// <summary>Source offset of the block's first character, for incremental re-parsing (§4.7).</summary>
    [JsonIgnore]
    public int SourceStart { get; internal set; }

    [JsonIgnore]
    public int Depth { get; internal set; }

    internal void AssignId(int index) => Id = "b" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class ParagraphBlock : Block
{
    [JsonPropertyName("inlines")]
    public IReadOnlyList<Inline> Inlines { get; init; } = [];
}

public sealed class HeadingBlock : Block
{
    [JsonPropertyName("level")]
    public int Level { get; init; }

    [JsonPropertyName("inlines")]
    public IReadOnlyList<Inline> Inlines { get; init; } = [];
}

public sealed class CodeBlock : Block
{
    [JsonPropertyName("lang")]
    public string? Lang { get; init; }

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("closed")]
    public bool Closed { get; init; }

    [JsonPropertyName("meta")]
    public CodeMeta Meta { get; init; } = new();
}

/// <summary>§2.1 — parsed only from a fence info string.</summary>
public sealed class CodeMeta
{
    [JsonPropertyName("filename")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileName { get; init; }

    [JsonPropertyName("line_numbers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool LineNumbers { get; init; }
}

public sealed class MathBlock : Block
{
    [JsonPropertyName("tex")]
    public string Tex { get; init; } = string.Empty;

    [JsonPropertyName("closed")]
    public bool Closed { get; init; }

    [JsonPropertyName("display")]
    public string Display { get; init; } = "block";
}

public sealed class MermaidBlock : Block
{
    [JsonPropertyName("src")]
    public string Src { get; init; } = string.Empty;

    [JsonPropertyName("closed")]
    public bool Closed { get; init; }

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = MermaidKinds.Other;
}

public static class MermaidKinds
{
    public const string Flowchart = "flowchart";
    public const string Sequence = "sequence";
    public const string Other = "other";

    /// <summary>§2 — only the first diagram keyword decides the kind; anything else is <c>other</c>.</summary>
    public static string Classify(string source)
    {
        var trimmed = source.TrimStart();
        if (trimmed.StartsWith("sequenceDiagram", StringComparison.Ordinal))
        {
            return Sequence;
        }

        return trimmed.StartsWith("graph", StringComparison.Ordinal) ||
               trimmed.StartsWith("flowchart", StringComparison.Ordinal)
            ? Flowchart
            : Other;
    }
}

public sealed class TableBlock : Block
{
    [JsonPropertyName("align")]
    public IReadOnlyList<string?> Align { get; init; } = [];

    [JsonPropertyName("header")]
    public IReadOnlyList<IReadOnlyList<Inline>> Header { get; init; } = [];

    [JsonPropertyName("rows")]
    public IReadOnlyList<IReadOnlyList<IReadOnlyList<Inline>>> Rows { get; init; } = [];
}

public static class TableAlign
{
    public const string Left = "left";
    public const string Center = "center";
    public const string Right = "right";
}

public sealed class ListBlock : Block
{
    [JsonPropertyName("ordered")]
    public bool Ordered { get; init; }

    [JsonPropertyName("start")]
    public int Start { get; init; } = 1;

    [JsonPropertyName("tight")]
    public bool Tight { get; init; }

    [JsonPropertyName("items")]
    public IReadOnlyList<ListItem> Items { get; init; } = [];
}

public sealed class ListItem
{
    [JsonPropertyName("checked")]
    public bool? Checked { get; init; }

    [JsonPropertyName("blocks")]
    public IReadOnlyList<Block> Blocks { get; init; } = [];
}

public sealed class QuoteBlock : Block
{
    [JsonPropertyName("blocks")]
    public IReadOnlyList<Block> Blocks { get; init; } = [];
}

public sealed class HrBlock : Block;

public sealed class HtmlBlock : Block
{
    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;
}

public sealed class FootnoteDefBlock : Block
{
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("blocks")]
    public IReadOnlyList<Block> Blocks { get; init; } = [];
}

public sealed class ImageBlock : Block
{
    [JsonPropertyName("src")]
    public string Src { get; init; } = string.Empty;

    [JsonPropertyName("alt")]
    public string Alt { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; init; }
}

public sealed class UnknownBlock : Block
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("raw")]
    public string Raw { get; init; } = string.Empty;
}

/// <summary>§3 — one of the 10 inline types.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextInline), "text")]
[JsonDerivedType(typeof(EmphInline), "emph")]
[JsonDerivedType(typeof(StrongInline), "strong")]
[JsonDerivedType(typeof(StrikeInline), "strike")]
[JsonDerivedType(typeof(CodeInline), "code")]
[JsonDerivedType(typeof(LinkInline), "link")]
[JsonDerivedType(typeof(ImageInline), "image")]
[JsonDerivedType(typeof(MathInline), "math")]
[JsonDerivedType(typeof(FootnoteRefInline), "footnote_ref")]
[JsonDerivedType(typeof(BrInline), "br")]
public abstract class Inline;

public sealed class TextInline : Inline
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

public sealed class EmphInline : Inline
{
    [JsonPropertyName("inlines")]
    public IReadOnlyList<Inline> Inlines { get; init; } = [];
}

public sealed class StrongInline : Inline
{
    [JsonPropertyName("inlines")]
    public IReadOnlyList<Inline> Inlines { get; init; } = [];
}

public sealed class StrikeInline : Inline
{
    [JsonPropertyName("inlines")]
    public IReadOnlyList<Inline> Inlines { get; init; } = [];
}

public sealed class CodeInline : Inline
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

public sealed class LinkInline : Inline
{
    [JsonPropertyName("dest")]
    public string Dest { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("inlines")]
    public IReadOnlyList<Inline> Inlines { get; init; } = [];
}

public sealed class ImageInline : Inline
{
    [JsonPropertyName("src")]
    public string Src { get; init; } = string.Empty;

    [JsonPropertyName("alt")]
    public string Alt { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; init; }
}

public sealed class MathInline : Inline
{
    [JsonPropertyName("tex")]
    public string Tex { get; init; } = string.Empty;
}

public sealed class FootnoteRefInline : Inline
{
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;
}

public sealed class BrInline : Inline;
