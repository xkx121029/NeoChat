package com.neochat.app.render

/** 行级小工具：缩进、空白、取行。 */
internal fun isBlank(line: String): Boolean = line.isBlank()

/** 行首空格数（制表符按 4 计）。 */
internal fun indentOf(line: String): Int {
    var n = 0
    for (ch in line) {
        when {
            ch == ' ' -> n++
            ch == '\t' -> n += TAB_WIDTH
            else -> return n
        }
    }
    return n
}

/** 去掉最多 [width] 个行首空格。 */
internal fun stripIndent(line: String, width: Int): String {
    var k = 0
    var removed = 0
    while (k < line.length && removed < width) {
        val ch = line[k]
        when {
            ch == ' ' -> removed++
            ch == '\t' -> removed += TAB_WIDTH
            else -> return line.substring(k)
        }
        k++
    }
    return line.substring(k)
}

/** 从 [from] 起第一个非空白行下标；全是空白返回 -1。 */
internal fun nextNonBlank(lines: List<String>, from: Int): Int {
    var k = from
    var found = -1
    while (k < lines.size && found < 0) {
        if (!isBlank(lines[k])) found = k
        k++
    }
    return found
}

/** 去掉尾部空行。 */
internal fun trimTrailingBlanks(lines: List<String>): List<String> {
    var end = lines.size
    while (end > 0 && isBlank(lines[end - 1])) end--
    return if (end == lines.size) lines else lines.subList(0, end)
}

/** 行内是否有未转义的 `|`（表格判定）。 */
internal fun hasUnescapedPipe(line: String): Boolean {
    var k = 0
    var found = false
    while (k < line.length && !found) {
        when (line[k]) {
            '\\' -> k += 2
            '|' -> found = true
            else -> k++
        }
    }
    return found
}

/** 按未转义的 `|` 切表格单元，去掉行首行尾的边框竖线。 */
internal fun splitCells(line: String): List<String> {
    val text = line.trim()
    val cells = mutableListOf<String>()
    val buf = StringBuilder()
    var k = 0
    while (k < text.length) {
        val escapedPipe = text[k] == '\\' && k + 1 < text.length && text[k + 1] == '|'
        when {
            escapedPipe -> {
                buf.append('|')
                k += 2
            }
            text[k] == '|' -> {
                cells.add(buf.toString())
                buf.setLength(0)
                k++
            }
            else -> {
                buf.append(text[k])
                k++
            }
        }
    }
    cells.add(buf.toString())
    if (text.startsWith("|")) cells.removeAt(0)
    if (cells.size > 1 && text.endsWith("|") && !text.endsWith("\\|")) cells.removeAt(cells.lastIndex)
    return cells.map { it.trim() }
}

private const val TAB_WIDTH: Int = 4
