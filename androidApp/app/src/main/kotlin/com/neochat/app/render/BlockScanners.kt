package com.neochat.app.render

/** 围栏代码块（§2 `code` / §2 `mermaid`），未闭合时 `closed=false`（§4.2）。 */
internal fun ParseScope.scanFence(lines: List<String>, i: Int, id: String): ScanStep? {
    val head = FENCE.matchEntire(lines[i]) ?: return null
    val marker = head.groupValues[1]
    val body = StringBuilder()
    var j = i + 1
    var closed = false
    while (j < lines.size) {
        if (isClosingFence(lines[j], marker)) {
            closed = true
            j++
            break
        }
        if (body.isNotEmpty()) body.append('\n')
        body.append(lines[j])
        j++
    }
    val text = body.toString()
    val info = head.groupValues[2].trim()
    val lang = info.takeWhile { !it.isWhitespace() }
    val block = if (lang.equals("mermaid", ignoreCase = true)) {
        mermaidBlock(id, text, closed)
    } else {
        val parsed = parseCodeFenceInfo(info)
        Block.Code(id, parsed.lang, text, closed, parsed.meta)
    }
    return ScanStep(block, j)
}

/** `kind=other` 的 mermaid 按 §2 降级为 `code`。 */
private fun mermaidBlock(id: String, src: String, closed: Boolean): Block {
    val kind = mermaidKindOf(src)
    return if (kind == MermaidKind.OTHER) {
        Block.Code(id, "mermaid", src, closed, CodeMeta())
    } else {
        Block.Mermaid(id, src, closed, kind)
    }
}

private fun isClosingFence(line: String, marker: String): Boolean {
    val text = line.trim()
    if (text.length < marker.length) return false
    val run = text.takeWhile { it == marker[0] }
    return run.length >= marker.length && run.length == text.length
}

/** 块级公式 `$$…$$` / `\[…\]`，未闭合时 `closed=false`（§4.3）。 */
internal fun ParseScope.scanMathBlock(lines: List<String>, i: Int, id: String): ScanStep? {
    val text = lines[i].trimStart()
    val open = when {
        text.startsWith("$$") -> "$$"
        text.startsWith("\\[") -> "\\["
        else -> return null
    }
    val close = if (open == "$$") "$$" else "\\]"
    val rest = text.substring(open.length)
    val inlineEnd = rest.indexOf(close)
    if (inlineEnd >= 0) return ScanStep(Block.Math(id, rest.substring(0, inlineEnd).trim(), true), i + 1)
    val body = StringBuilder(rest)
    var j = i + 1
    var closed = false
    while (j < lines.size) {
        val end = lines[j].indexOf(close)
        if (end >= 0) {
            body.append('\n').append(lines[j], 0, end)
            closed = true
            j++
            break
        }
        body.append('\n').append(lines[j])
        j++
    }
    return ScanStep(Block.Math(id, body.toString().trim('\n'), closed), j)
}

/** ATX 标题（§2 `heading`）。 */
internal fun ParseScope.scanAtxHeading(lines: List<String>, i: Int, id: String): ScanStep? {
    val head = ATX.matchEntire(lines[i]) ?: return null
    val level = head.groupValues[1].length
    val content = head.groupValues[2].trimEnd('#').trim()
    return ScanStep(Block.Heading(id, level, inline.parse(content, 0)), i + 1)
}

/** 分隔线（§2 `hr`）。 */
internal fun ParseScope.scanRule(lines: List<String>, i: Int, id: String): ScanStep? {
    if (!RULE.matches(lines[i])) return null
    return ScanStep(Block.Rule(id), i + 1)
}

/** 引用块（§2 `quote`）；空白行没有 `>` 就结束引用。 */
internal fun ParseScope.scanQuote(lines: List<String>, i: Int, depth: Int, id: String): ScanStep? {
    if (!QUOTE.containsMatchIn(lines[i])) return null
    val inner = mutableListOf<String>()
    var j = i
    while (j < lines.size) {
        val match = QUOTE.find(lines[j]) ?: break
        inner.add(lines[j].substring(match.range.last + 1))
        j++
    }
    val blocks = if (depth + 1 > limits.maxNesting) {
        listOf(Block.Unknown(id, "quote", lines.subList(i, j).joinToString("\n")))
    } else {
        parseBlocks(inner, depth + 1, "$id.", 0)
    }
    return ScanStep(Block.Quote(id, blocks), j)
}

/** 脚注定义 `[^label]: …`（§2 `footnote_def`）。 */
internal fun ParseScope.scanFootnote(lines: List<String>, i: Int, depth: Int, id: String): ScanStep? {
    val head = FOOTNOTE.matchEntire(lines[i]) ?: return null
    val body = mutableListOf(head.groupValues[2])
    var j = i + 1
    var running = true
    while (running && j < lines.size) {
        val line = lines[j]
        if (isBlank(line)) {
            body.add("")
            j++
        } else if (indentOf(line) >= FOOTNOTE_CONTINUATION) {
            body.add(line.trimStart())
            j++
        } else {
            running = false
        }
    }
    val blocks = if (depth + 1 > limits.maxNesting) {
        listOf(Block.Unknown("$id.0", "footnote", head.groupValues[2]))
    } else {
        parseBlocks(trimTrailingBlanks(body), depth + 1, "$id.", 0)
    }
    return ScanStep(Block.FootnoteDef(id, head.groupValues[1], blocks), j)
}

/** 原始 HTML 块：整段按字面保存，不解析不执行（§7）。 */
internal fun ParseScope.scanHtml(lines: List<String>, i: Int, id: String): ScanStep? {
    if (!HTML_START.containsMatchIn(lines[i])) return null
    var j = i + 1
    while (j < lines.size && !isBlank(lines[j])) j++
    return ScanStep(Block.Html(id, lines.subList(i, j).joinToString("\n")), j)
}

/** 引用式链接定义 `[label]: url`：v1 不解析，归入 `unknown{kind:"link_definition"}`（§2.3）。 */
internal fun ParseScope.scanLinkDefinition(lines: List<String>, i: Int, id: String): ScanStep? {
    if (!LINK_DEF.matches(lines[i])) return null
    return ScanStep(Block.Unknown(id, "link_definition", lines[i].trim()), i + 1)
}

/** 脚注续行要求的最小缩进。 */
private const val FOOTNOTE_CONTINUATION: Int = 2

internal val FENCE: Regex = Regex("""^ {0,3}(`{3,}|~{3,})\s*(.*)""")
internal val ATX: Regex = Regex("""^ {0,3}(#{1,6})(?:\s+(.*?))?\s*""")
internal val RULE: Regex = Regex("""^ {0,3}(?:(?:\*\s*){3,}|(?:-\s*){3,}|(?:_\s*){3,})\s*""")
internal val QUOTE: Regex = Regex("""^ {0,3}> ?""")
internal val HTML_START: Regex = Regex("""^ {0,3}<(?:[A-Za-z!?/])""")
internal val FOOTNOTE: Regex = Regex("""^ {0,3}\[\^([^\]\s]+)]:\s*(.*)""")
internal val LINK_DEF: Regex = Regex("""^ {0,3}\[[^\]]+]:\s*\S+.*""")
