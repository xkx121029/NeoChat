package com.neochat.app.render

/** spec/render-model.md §1 的 `v`。 */
public const val RENDER_MODEL_VERSION: Int = 1

/**
 * spec/render-model.md v1.0.0 §1 信封：`blocks` 是闭集，`flags` 是闭集。
 *
 * 块 `id` 由解析器按块序号分配（§4.6）：顶层 `b0,b1,…`；嵌套块为 `b0.1` 形式（父块 id + "." + 子序号）。
 * 渲染器据此复用视图。
 */
public data class RenderModel(
    public val blocks: List<Block> = emptyList(),
    public val flags: RenderFlags = RenderFlags(),
) {
    /** §1 `v`。 */
    public val version: Int
        get() = RENDER_MODEL_VERSION
}

/** §1 `flags`（闭集）。 */
public data class RenderFlags(
    /** §1：流仍在进行，最后一个块可能还会增长。 */
    public val incomplete: Boolean = false,
    public val hasMath: Boolean = false,
    public val hasMermaid: Boolean = false,
    public val hasTable: Boolean = false,
    public val hasCode: Boolean = false,
    public val hasList: Boolean = false,
    public val hasFootnote: Boolean = false,
    /** list / quote 的最大嵌套深度。 */
    public val maxDepth: Int = 0,
    /** §5：超过解析上限，尾部被丢弃。 */
    public val truncated: Boolean = false,
)

/** §2 Block 闭集（13 种）。 */
public sealed interface Block {
    /** §4.6 稳定块 id。 */
    public val id: String

    /** §2 `paragraph`。 */
    public data class Paragraph(override val id: String, public val inlines: List<Inline>) : Block

    /** §2 `heading`（ATX 与 Setext 归入同一类型）。 */
    public data class Heading(override val id: String, public val level: Int, public val inlines: List<Inline>) : Block

    /** §2 `code`；`meta` 只从围栏 info string 解析（§2.1）。 */
    public data class Code(
        override val id: String,
        public val lang: String?,
        public val text: String,
        public val closed: Boolean,
        public val meta: CodeMeta = CodeMeta(),
    ) : Block

    /** §2 `math`，块级 `$$…$$` 或 `\[…\]`。 */
    public data class Math(override val id: String, public val tex: String, public val closed: Boolean) : Block

    /** §2 `mermaid`；`kind=OTHER` 按 §2 降级为 `code`。 */
    public data class Mermaid(
        override val id: String,
        public val src: String,
        public val closed: Boolean,
        public val kind: MermaidKind,
    ) : Block

    /** §2 `table`（GFM）。 */
    public data class Table(
        override val id: String,
        public val align: List<Align?>,
        public val header: List<List<Inline>>,
        public val rows: List<List<List<Inline>>>,
    ) : Block

    /** §2 `list`；`items[].checked` 非 null 即任务列表。 */
    public data class ListBlock(
        override val id: String,
        public val ordered: Boolean,
        public val start: Int,
        public val tight: Boolean,
        public val items: List<ListItem>,
    ) : Block

    /** §2 `quote`，可嵌套。 */
    public data class Quote(override val id: String, public val blocks: List<Block>) : Block

    /** §2 `hr` 分隔线。 */
    public data class Rule(override val id: String) : Block

    /** §2 `html`：原始 HTML 一律转义为字面文本。 */
    public data class Html(override val id: String, public val source: String) : Block

    /** §2 `footnote_def`。 */
    public data class FootnoteDef(
        override val id: String,
        public val label: String,
        public val blocks: List<Block>,
    ) : Block

    /** §2 `image` 块级图片，判定见 §2.2。 */
    public data class Image(
        override val id: String,
        public val src: String,
        public val alt: String,
        public val title: String?,
    ) : Block

    /** §2 `unknown`：v1 未定义语法的原样保留。 */
    public data class Unknown(override val id: String, public val kind: String, public val raw: String) : Block
}

/** §2 `code` 的 `meta`。 */
public data class CodeMeta(
    public val filename: String? = null,
    public val lineNumbers: Boolean = false,
)

/** §2 `list` 的一项。 */
public data class ListItem(public val checked: Boolean?, public val blocks: List<Block>)

/** §2 `table.align` 的一格；null 表示不指定。 */
public enum class Align { LEFT, CENTER, RIGHT }

/** §2 `mermaid.kind`。 */
public enum class MermaidKind { FLOWCHART, SEQUENCE, OTHER }

/** §3 Inline 闭集（10 种）。 */
public sealed interface Inline {
    /** §3 `text`。 */
    public data class Text(public val text: String) : Inline

    /** §3 `emph`。 */
    public data class Emph(public val inlines: List<Inline>) : Inline

    /** §3 `strong`。 */
    public data class Strong(public val inlines: List<Inline>) : Inline

    /** §3 `strike`。 */
    public data class Strike(public val inlines: List<Inline>) : Inline

    /** §3 `code`。 */
    public data class Code(public val text: String) : Inline

    /** §3 `link`。 */
    public data class Link(
        public val dest: String,
        public val title: String?,
        public val inlines: List<Inline>,
    ) : Inline

    /** §3 `image`。 */
    public data class Image(public val src: String, public val alt: String, public val title: String?) : Inline

    /** §3 `math`（行内 `$…$` / `\(…\)`）。 */
    public data class Math(public val tex: String) : Inline

    /** §3 `footnote_ref`。 */
    public data class FootnoteRef(public val label: String) : Inline

    /** §3 `br` 硬换行。 */
    public data object Br : Inline
}
