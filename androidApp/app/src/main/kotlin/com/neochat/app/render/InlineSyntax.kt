package com.neochat.app.render

/** `[text](dest "title")` 的解引用结果。 */
internal class InlineTarget(val dest: String, val title: String?)

/** 顺带返回闭合下标的结果。 */
internal class TaggedTarget(val dest: String, val title: String?, val end: Int)

/** 找 [open] 与 [close] 的配对下标（跳过反斜杠转义）；找不到返回 -1。 */
internal fun matchingIndex(src: String, from: Int, open: Char, close: Char): Int {
    var depth = 0
    var k = from
    while (k < src.length) {
        val ch = src[k]
        if (ch == '\\') {
            k += 2
            continue
        }
        if (ch == open) depth++
        if (ch == close) {
            depth--
            if (depth == 0) return k
        }
        k++
    }
    return -1
}

/** `ch` 在 [from] 起的连写结束下标。 */
internal fun runEndIndex(src: String, from: Int, ch: Char): Int {
    var k = from
    while (k < src.length && src[k] == ch) k++
    return k
}

/** §3 code span 的内容归一：换行变空格，内容前后各一个空格时去掉。 */
internal fun codeSpanText(raw: String): String {
    val inner = raw.replace('\n', ' ')
    val padded = inner.length > 2 && inner.first() == ' ' && inner.last() == ' '
    return if (padded && inner.isNotBlank()) inner.substring(1, inner.length - 1) else inner
}

/** 解析 `dest "title"` / `<dest> "title"` 形式的目标。 */
internal fun parseLinkTarget(raw: String): InlineTarget? {
    val text = raw.trim()
    if (text.isEmpty()) return null
    if (text.startsWith("<")) {
        val close = text.indexOf('>')
        return if (close < 0) null else InlineTarget(text.substring(1, close), linkTitleOf(text.substring(close + 1)))
    }
    val split = text.indexOfFirst { it == ' ' || it == '\t' || it == '\n' }
    return if (split < 0) {
        InlineTarget(text, null)
    } else {
        InlineTarget(text.substring(0, split), linkTitleOf(text.substring(split)))
    }
}

private fun linkTitleOf(raw: String): String? {
    val text = raw.trim()
    if (text.length < 2) return null
    val first = text.first()
    val last = text.last()
    val paired = (first == '"' && last == '"') || (first == '\'' && last == '\'') || (first == '(' && last == ')')
    return if (paired) text.substring(1, text.length - 1) else null
}
