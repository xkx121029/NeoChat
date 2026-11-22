package com.neochat.app.render

/**
 * Markdown → RenderModel（spec/render-model.md v1.0.0）。
 *
 * §4.1 永不抛异常：任何输入都产出模型，无法识别的语法降级为 `unknown` 或 `code{closed:false}`。
 * §4.5 前缀稳定性：每个块只在它被确定后才产出，开放块永远是最后一个块。
 */
public class MarkdownParser(internal val limits: RenderLimits = RenderLimits.Default) {

    /** 全文解析；[incomplete] 写进 `flags.incomplete`（§1）。 */
    public fun parse(source: String, incomplete: Boolean = false): RenderModel {
        val clipped = clipSource(source, limits)
        val scope = newScope(clipped, incomplete)
        val blocks = scope.parseBlocks(LineTable(clipped, 0).lines, 0, TOP_LEVEL_PREFIX, 0)
        return assemble(blocks, incomplete, clipped.length < source.length || scope.truncated)
    }

    /**
     * §4.7 增量：只从 [from]（必须是行首）重解析尾部，块 id 从 [idBase] 续起。
     * 调用方保证 [from] 之前的内容没有变化。
     */
    internal fun parseTail(source: String, from: Int, idBase: Int, incomplete: Boolean): TailSlice {
        val clipped = clipSource(source, limits)
        val table = LineTable(clipped, from)
        val scope = newScope(clipped, incomplete)
        val starts = mutableListOf<Int>()
        scope.record = { _, lineIndex -> starts.add(table.starts[lineIndex]) }
        val blocks = scope.parseBlocks(table.lines, 0, TOP_LEVEL_PREFIX, idBase)
        return TailSlice(blocks, starts.toList(), scope.truncated)
    }

    /**
     * §4.5 的一个必要补丁：流未结束时，末尾那一行还没有换行，
     * 孤立的列表标记（`-` / `1.`）此刻既不是列表项也不该另起段落——它若先成块后并入列表，
     * 会让"已完成块"里多出又消失一个块。这类残行在流式期间直接跳过，换行到达后照常解析。
     */
    private fun newScope(clipped: String, incomplete: Boolean): ParseScope {
        val scope = ParseScope(limits, InlineParser(limits))
        scope.suppressTrailingMarker = incomplete && clipped.isNotEmpty() && clipped.last() != '\n'
        return scope
    }

    /** §5 块数上限与 `truncated` 汇总。 */
    internal fun assemble(blocks: List<Block>, incomplete: Boolean, truncated: Boolean): RenderModel {
        val kept = if (blocks.size > limits.maxBlocks) blocks.subList(0, limits.maxBlocks) else blocks
        val flags = flagsOf(kept, incomplete, truncated || kept.size < blocks.size)
        return RenderModel(kept, flags)
    }

    internal fun flagsOf(blocks: List<Block>, incomplete: Boolean, truncated: Boolean): RenderFlags {
        val scan = FlagScan()
        scan.walk(blocks, 0)
        return RenderFlags(
            incomplete = incomplete,
            hasMath = scan.hasMath,
            hasMermaid = scan.hasMermaid,
            hasTable = scan.hasTable,
            hasCode = scan.hasCode,
            hasList = scan.hasList,
            hasFootnote = scan.hasFootnote,
            maxDepth = scan.maxDepth,
            truncated = truncated,
        )
    }
}

/** 顶层块 id 前缀（§4.6 `b0,b1,…`）。 */
internal const val TOP_LEVEL_PREFIX: String = "b"

/** 尾部重解析的结果；`starts` 是各块在全文里的起始字符偏移。 */
internal data class TailSlice(val blocks: List<Block>, val starts: List<Int>, val truncated: Boolean)

/** 一个块扫描器的产出：块本身与下一行下标。 */
internal class ScanStep(val block: Block, val next: Int)

/** 按行切分并记录每行的绝对起始偏移（只切 [from] 之后的部分）。 */
internal class LineTable(source: String, from: Int) {
    val lines: List<String>
    val starts: IntArray

    init {
        val texts = ArrayList<String>()
        val offsets = ArrayList<Int>()
        var start = from
        var k = from
        while (true) {
            val atEnd = k >= source.length
            if (atEnd || source[k] == '\n') {
                var end = if (atEnd) source.length else k
                if (end > start && source[end - 1] == '\r') end--
                texts.add(source.substring(start, end))
                offsets.add(start)
                if (atEnd) break
                start = k + 1
            }
            k++
        }
        lines = texts
        starts = offsets.toIntArray()
    }
}

/**
 * 块级解析的共享状态：上限、内联解析器、顶层块起始偏移的记录点。
 * 同一实例的递归调用（list/quote/footnote 内部）不记录偏移（§4.6 的 id 只对块序列负责）。
 */
internal class ParseScope(
    internal val limits: RenderLimits,
    internal val inline: InlineParser,
) {
    internal var record: ((Block, Int) -> Unit)? = null
    internal var truncated: Boolean = false
    internal var suppressTrailingMarker: Boolean = false

    internal fun parseBlocks(lines: List<String>, depth: Int, idPrefix: String, idBase: Int): List<Block> {
        val out = ArrayList<Block>()
        var i = 0
        var running = true
        while (running && i < lines.size) {
            val line = lines[i]
            val skipped = isBlank(line) ||
                (suppressTrailingMarker && i == lines.lastIndex && LIST_MARKER_ONLY.matches(line))
            val step = if (skipped) null else scanAt(lines, i, depth, "$idPrefix${idBase + out.size}")
            when {
                skipped -> i++
                out.size >= limits.maxBlocks -> {
                    truncated = true
                    running = false
                }
                step != null -> {
                    out.add(step.block)
                    if (depth == 0) record?.invoke(step.block, i)
                    i = step.next
                }
                else -> running = false
            }
        }
        return out
    }

    private fun scanAt(lines: List<String>, i: Int, depth: Int, id: String): ScanStep? =
        scanFence(lines, i, id)
            ?: scanMathBlock(lines, i, id)
            ?: scanAtxHeading(lines, i, id)
            ?: scanRule(lines, i, id)
            ?: scanQuote(lines, i, depth, id)
            ?: scanList(lines, i, depth, id)
            ?: scanFootnote(lines, i, depth, id)
            ?: scanTable(lines, i, id)
            ?: scanHtml(lines, i, id)
            ?: scanLinkDefinition(lines, i, id)
            ?: scanParagraph(lines, i, id)
}

/** `flags` 的递归统计。 */
private class FlagScan {
    var hasMath: Boolean = false
    var hasMermaid: Boolean = false
    var hasTable: Boolean = false
    var hasCode: Boolean = false
    var hasList: Boolean = false
    var hasFootnote: Boolean = false
    var maxDepth: Int = 0

    fun walk(blocks: List<Block>, depth: Int) {
        for (block in blocks) {
            when (block) {
                is Block.Math -> hasMath = true
                is Block.Mermaid -> hasMermaid = true
                is Block.Table -> hasTable = true
                is Block.Code -> hasCode = true
                is Block.ListBlock -> {
                    hasList = true
                    descend(block.items.flatMap { it.blocks }, depth + 1)
                }
                is Block.Quote -> descend(block.blocks, depth + 1)
                is Block.FootnoteDef -> {
                    hasFootnote = true
                    descend(block.blocks, depth + 1)
                }
                is Block.Paragraph -> if (block.inlines.any { it is Inline.Math }) hasMath = true
                is Block.Heading -> if (block.inlines.any { it is Inline.Math }) hasMath = true
                else -> Unit
            }
        }
    }

    private fun descend(blocks: List<Block>, depth: Int) {
        if (depth > maxDepth) maxDepth = depth
        walk(blocks, depth)
    }
}
