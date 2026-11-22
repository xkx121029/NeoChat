package com.neochat.app.render

/** GFM 表格（§2 `table`）：列数按 header 对齐，超出上限的列丢弃（§5）。 */
internal fun ParseScope.scanTable(lines: List<String>, i: Int, id: String): ScanStep? {
    if (!hasUnescapedPipe(lines[i]) || i + 1 >= lines.size) return null
    val aligns = delimiterAligns(lines[i + 1]) ?: return null
    return tableStep(lines, i, id, aligns)
}

private fun ParseScope.tableStep(lines: List<String>, i: Int, id: String, aligns: List<Align?>): ScanStep? {
    val headerCells = splitCells(lines[i])
    return if (headerCells.size == aligns.size) buildTable(lines, i, id, aligns, headerCells) else null
}

private fun ParseScope.buildTable(
    lines: List<String>,
    i: Int,
    id: String,
    aligns: List<Align?>,
    headerCells: List<String>,
): ScanStep {
    val columns = minOf(aligns.size, limits.maxTableColumns)
    if (columns < aligns.size) truncated = true
    val header = (0 until columns).map { column -> inline.parse(headerCells[column], 0) }
    val rows = ArrayList<List<List<Inline>>>()
    var j = i + 2
    var more = isRow(lines, j)
    while (more && rows.size < limits.maxTableRows) {
        val cells = splitCells(lines[j])
        rows.add((0 until columns).map { column -> inline.parse(cells.getOrElse(column) { "" }, 0) })
        j++
        more = isRow(lines, j)
    }
    if (more) truncated = true
    return ScanStep(Block.Table(id, aligns.take(columns), header, rows), j)
}

private fun isRow(lines: List<String>, j: Int): Boolean =
    j < lines.size && !isBlank(lines[j]) && hasUnescapedPipe(lines[j])

private fun delimiterAligns(line: String): List<Align?>? {
    val cells = splitCells(line)
    if (cells.isEmpty()) return null
    val aligns = ArrayList<Align?>(cells.size)
    for (cell in cells) {
        if (!DELIM_CELL.matches(cell)) return null
        aligns.add(
            when {
                cell.startsWith(":") && cell.endsWith(":") -> Align.CENTER
                cell.endsWith(":") -> Align.RIGHT
                cell.startsWith(":") -> Align.LEFT
                else -> null
            },
        )
    }
    return aligns
}

/** 列表（§2 `list`）：有序/无序/任务/嵌套；空行把列表变松散（`tight=false`）。 */
internal fun ParseScope.scanList(lines: List<String>, i: Int, depth: Int, id: String): ScanStep? {
    val orderedHead = ORDERED.matchEntire(lines[i])
    val head = orderedHead ?: BULLET.matchEntire(lines[i]) ?: return null
    val ordered = orderedHead != null
    val baseIndent = head.groupValues[1].length
    val contentOffset = head.groups.last()?.range?.first ?: (baseIndent + 2)
    val start = if (ordered) orderedHead.groupValues[2].toIntOrNull() ?: 1 else 1
    val shape = ListShape(ordered, baseIndent, contentOffset, depth, id)
    val items = ArrayList<ListItem>()
    var tight = true
    var j = i
    var running = true
    while (running && j < lines.size && items.size < limits.maxBlocks) {
        val step = readItem(lines, j, shape, items.size)
        if (step == null) {
            running = false
        } else {
            items.add(step.item)
            if (step.loose) tight = false
            j = step.next
        }
    }
    return ScanStep(Block.ListBlock(id, ordered, start, tight, items), j)
}

/** 一份列表的解析形状：同层缩进、内容列、嵌套层与块 id。 */
private class ListShape(
    val ordered: Boolean,
    val baseIndent: Int,
    val contentOffset: Int,
    val depth: Int,
    val id: String,
)

/** 读一项；不是同一层的列表项就返回 null，列表到此结束。 */
private fun ParseScope.readItem(lines: List<String>, at: Int, shape: ListShape, index: Int): Item? {
    val head = itemAt(lines[at], shape.ordered) ?: return null
    if (head.indent != shape.baseIndent) return null
    val task = TASK.matchEntire(head.content)
    val body = mutableListOf(task?.groups?.get(2)?.value ?: head.content)
    val collected = collectItem(lines, at + 1, shape)
    body.addAll(collected.lines)
    val loose = collected.separated || trimTrailingBlanks(body).any { isBlank(it) }
    val checked = task?.let { it.groupValues[1] != " " }
    return Item(ListItem(checked, itemBlocks(body, shape, index)), collected.next, loose)
}

private fun ParseScope.itemBlocks(body: List<String>, shape: ListShape, index: Int): List<Block> {
    if (shape.depth + 1 > limits.maxNesting) {
        return listOf(Block.Unknown("${shape.id}.$index.0", "list", body.joinToString("\n")))
    }
    return parseBlocks(trimTrailingBlanks(body), shape.depth + 1, "${shape.id}.$index.", 0)
}

/** 收集一项的续行；`separated=true` 表示这一项与下一项之间有空行。 */
private fun ParseScope.collectItem(lines: List<String>, from: Int, shape: ListShape): ItemBody {
    val body = mutableListOf<String>()
    var j = from
    var separated = false
    var running = true
    while (running && j < lines.size) {
        when (lineDecision(lines, j, shape)) {
            LineDecision.CONTINUE -> {
                body.add(stripIndent(lines[j], shape.contentOffset))
                j += 1
            }
            LineDecision.BLANK -> {
                body.add("")
                j += 1
            }
            LineDecision.ENDS_AFTER_ITEM -> {
                separated = true
                j = nextNonBlank(lines, j).coerceAtLeast(j + 1)
                running = false
            }
            LineDecision.ENDS -> {
                val next = nextNonBlank(lines, j)
                j = if (next < 0) lines.size else next
                running = false
            }
        }
    }
    return ItemBody(body, j, separated)
}

/** 一项内一行怎么走：续行 / 项内空行 / 空行后是同层新项 / 列表结束。 */
private fun lineDecision(lines: List<String>, j: Int, shape: ListShape): LineDecision {
    val line = lines[j]
    if (!isBlank(line)) {
        val isItem = itemAt(line, shape.ordered)?.indent == shape.baseIndent
        return if (!isItem && indentOf(line) > shape.baseIndent) LineDecision.CONTINUE else LineDecision.ENDS
    }
    val next = nextNonBlank(lines, j)
    val nextItem = next >= 0 && itemAt(lines[next], shape.ordered)?.indent == shape.baseIndent
    val nextDeeper = next >= 0 && indentOf(lines[next]) > shape.baseIndent
    return when {
        nextItem -> LineDecision.ENDS_AFTER_ITEM
        nextDeeper -> LineDecision.BLANK
        else -> LineDecision.ENDS
    }
}

private enum class LineDecision { CONTINUE, BLANK, ENDS_AFTER_ITEM, ENDS }

/** 段落与 Setext 标题（§2 `paragraph` / `heading`），以及 §2.2 的块级图片判定。 */
internal fun ParseScope.scanParagraph(lines: List<String>, i: Int, id: String): ScanStep? {
    val body = mutableListOf(lines[i])
    var j = i + 1
    var setext = 0
    var running = true
    while (running && j < lines.size) {
        val line = lines[j]
        if (isBlank(line)) {
            running = false
        } else {
            val level = setextLevel(line)
            when {
                level > 0 -> {
                    setext = level
                    j += 1
                    running = false
                }
                startsBlock(lines, j) -> running = false
                else -> {
                    body.add(line)
                    j += 1
                }
            }
        }
    }
    val inlines = inline.parse(body.joinToString("\n"), 0)
    val image = inlines.singleOrNull() as? Inline.Image
    val block = when {
        setext > 0 -> Block.Heading(id, setext, inlines)
        body.size == 1 && image != null -> Block.Image(id, image.src, image.alt, image.title)
        else -> Block.Paragraph(id, inlines)
    }
    return ScanStep(block, j)
}

private fun setextLevel(line: String): Int {
    val match = SETEXT.matchEntire(line) ?: return 0
    return if (match.groupValues[1].startsWith("=")) 1 else 2
}

/** 该行是否开启一个非段落块（用于段落的中断判定）。 */
private fun startsBlock(lines: List<String>, j: Int): Boolean {
    val line = lines[j]
    return FENCE.containsMatchIn(line) ||
        isMathStart(line) ||
        ATX.containsMatchIn(line) ||
        RULE.matches(line) ||
        QUOTE.containsMatchIn(line) ||
        BULLET.matches(line) ||
        ORDERED.matches(line) ||
        FOOTNOTE.containsMatchIn(line) ||
        LINK_DEF.matches(line) ||
        HTML_START.containsMatchIn(line) ||
        isTableStart(lines, j)
}

private fun isTableStart(lines: List<String>, j: Int): Boolean =
    hasUnescapedPipe(lines[j]) && j + 1 < lines.size && delimiterAligns(lines[j + 1]) != null

private fun isMathStart(line: String): Boolean {
    val text = line.trimStart()
    return text.startsWith("$$") || text.startsWith("\\[")
}

/** 一行列表项：缩进、内容、内容起点。 */
private class ItemHead(val indent: Int, val content: String, val contentStart: Int)

private fun itemAt(line: String, ordered: Boolean): ItemHead? {
    val match = if (ordered) ORDERED.matchEntire(line) else BULLET.matchEntire(line)
    val group = match?.groups?.last() ?: return null
    return ItemHead(match.groupValues[1].length, group.value, group.range.first)
}

/** 一项的读取结果；`readItem` 返回 null 表示列表结束。 */
private class Item(val item: ListItem, val next: Int, val loose: Boolean)

private class ItemBody(val lines: List<String>, val next: Int, val separated: Boolean)

/** 流式期间末尾的"孤零零列表标记"残行（§4.5 的补丁用）。 */
internal val LIST_MARKER_ONLY: Regex = Regex("""^ {0,3}([-*+]|\d{1,9}[.)]?)\s*""")
private val BULLET: Regex = Regex("""^( {0,3})([-*+])( +)(.*)""")
private val ORDERED: Regex = Regex("""^( {0,3})(\d{1,9})([.)])( +)(.*)""")
private val TASK: Regex = Regex("""^\[([ xX])]\s*(.*)""")
private val SETEXT: Regex = Regex("""^ {0,3}(=+|-+)\s*""")
private val DELIM_CELL: Regex = Regex("""^:?-+:?$""")
