package com.neochat.app.render

/**
 * §3 Inline 解析：一次线性扫描，遇到未闭合标记就退回字面（§4.1 永不抛异常）。
 * 超过 [RenderLimits.maxInlineDepth] 层时余下部分降级为 `text`（保留原始标记字符）。
 */
internal class InlineParser(private val limits: RenderLimits) {

    fun parse(source: String, depth: Int): List<Inline> {
        if (source.isEmpty()) return emptyList()
        if (depth >= limits.maxInlineDepth) return listOf(Inline.Text(source))
        val out = ArrayList<Inline>()
        val buf = StringBuilder()
        var i = 0
        while (i < source.length) {
            val hit = attempt(source, i, depth)
            if (hit != null) {
                flush(buf, out)
                out.add(hit.value)
                i = hit.next
            } else if (source[i] == '\n') {
                flushBreak(buf, out)
                i++
            } else {
                buf.append(source[i])
                i++
            }
        }
        flush(buf, out)
        return merge(out)
    }

    private fun attempt(src: String, i: Int, depth: Int): Match? = when (src[i]) {
        '\\' -> escaped(src, i)
        '`' -> codeSpan(src, i)
        '$' -> dollarMath(src, i)
        '~' -> strike(src, i, depth)
        '*', '_' -> emphasis(src, i, depth)
        '!' -> image(src, i)
        '[' -> bracket(src, i, depth)
        else -> null
    }

    private fun escaped(src: String, i: Int): Match? {
        if (i + 1 >= src.length) return null
        val next = src[i + 1]
        val math = when (next) {
            '(' -> delimitedMath(src, i, "\\(", "\\)")
            '[' -> delimitedMath(src, i, "\\[", "\\]")
            else -> null
        }
        if (math != null) return math
        return when {
            next == '\n' -> Match(Inline.Br, i + 2)
            next in ASCII_PUNCTUATION -> Match(Inline.Text(next.toString()), i + 2)
            else -> null
        }
    }

    private fun delimitedMath(src: String, i: Int, open: String, close: String): Match? {
        val end = src.indexOf(close, i + open.length)
        if (end < 0) return null
        val tex = src.substring(i + open.length, end).trim()
        return if (tex.isEmpty()) null else Match(Inline.Math(tex), end + close.length)
    }

    private fun codeSpan(src: String, i: Int): Match? {
        val openEnd = runEndIndex(src, i, '`')
        val width = openEnd - i
        var k = openEnd
        while (k < src.length) {
            if (src[k] != '`') {
                k++
                continue
            }
            val end = runEndIndex(src, k, '`')
            if (end - k == width) return Match(Inline.Code(codeSpanText(src.substring(openEnd, k))), end)
            k = end
        }
        return null
    }

    private fun dollarMath(src: String, i: Int): Match? {
        val open = if (src.startsWith("$$", i)) "$$" else "$"
        val start = i + open.length
        val end = src.indexOf(open, start)
        if (end < 0) return null
        val tex = src.substring(start, end)
        val usable = tex.isNotEmpty() && tex.indexOf('\n') < 0 &&
            !tex.first().isWhitespace() && !tex.last().isWhitespace()
        return if (usable) Match(Inline.Math(tex), end + open.length) else null
    }

    private fun strike(src: String, i: Int, depth: Int): Match? {
        if (!src.startsWith("~~", i)) return null
        val end = src.indexOf("~~", i + 2)
        if (end < 0) return null
        val inner = src.substring(i + 2, end)
        val usable = inner.isNotBlank() && !inner.first().isWhitespace() && !inner.last().isWhitespace()
        return if (usable) Match(Inline.Strike(parse(inner, depth + 1)), end + 2) else null
    }

    private fun emphasis(src: String, i: Int, depth: Int): Match? {
        val marker = src[i]
        val strong = runEndIndex(src, i, marker) - i >= 2
        val close = if (strong) closingRun(src, i + 2, marker, 2) else -1
        return if (close > i + 2) {
            Match(Inline.Strong(parse(src.substring(i + 2, close), depth + 1)), close + 2)
        } else {
            singleEmphasis(src, i, depth, marker)
        }
    }

    private fun singleEmphasis(src: String, i: Int, depth: Int, marker: Char): Match? {
        val intraword = marker == '_' && i > 0 && src[i - 1].isLetterOrDigit()
        val openable = i + 1 < src.length && !src[i + 1].isWhitespace() && !intraword
        if (!openable) return null
        val end = closingRun(src, i + 1, marker, 1)
        return if (end <= i + 1) null else Match(Inline.Emph(parse(src.substring(i + 1, end), depth + 1)), end + 1)
    }

    /** 找 [marker] 的 [width] 连写闭合点；没有就返回 -1。 */
    private fun closingRun(src: String, from: Int, marker: Char, width: Int): Int {
        var k = from
        var found = -1
        while (k < src.length && found < 0) {
            val end = if (src[k] == marker) runEndIndex(src, k, marker) else k + 1
            if (acceptsClose(src, k, end, marker, width)) found = k
            k = end
        }
        return found
    }

    private fun acceptsClose(src: String, start: Int, end: Int, marker: Char, width: Int): Boolean {
        val wideEnough = end - start >= width
        val afterText = !src[start - 1].isWhitespace()
        val notDoubleRun = !(width == 1 && end > start + 1)
        val underscoreBoundary = marker != '_' || end >= src.length || !src[end].isLetterOrDigit()
        return src[start] == marker && wideEnough && afterText && notDoubleRun && underscoreBoundary
    }

    private fun image(src: String, i: Int): Match? {
        val labelEnd = if (i + 1 < src.length && src[i + 1] == '[') matchingIndex(src, i + 1, '[', ']') else -1
        val target = if (labelEnd > 0) targetAfter(src, labelEnd) else null
        return if (target == null) null else {
            Match(Inline.Image(target.dest, src.substring(i + 2, labelEnd), target.title), target.end)
        }
    }

    private fun bracket(src: String, i: Int, depth: Int): Match? {
        if (i + 1 < src.length && src[i + 1] == '^') return footnoteRef(src, i)
        val labelEnd = matchingIndex(src, i, '[', ']')
        val target = if (labelEnd > 0) targetAfter(src, labelEnd) else null
        return if (target == null) null else {
            Match(Inline.Link(target.dest, target.title, parse(src.substring(i + 1, labelEnd), depth + 1)), target.end)
        }
    }

    private fun footnoteRef(src: String, i: Int): Match? {
        val end = src.indexOf(']', i + 2)
        return if (end < 0) null else Match(Inline.FootnoteRef(src.substring(i + 2, end)), end + 1)
    }

    /** `[text](dest "title")` 里的 `(dest "title")`，返回带结束下标的解引用结果。 */
    private fun targetAfter(src: String, labelEnd: Int): Tagged? {
        val opens = labelEnd + 1 < src.length && src[labelEnd + 1] == '('
        val close = if (opens) matchingIndex(src, labelEnd + 1, '(', ')') else -1
        val target = if (close > 0) parseLinkTarget(src.substring(labelEnd + 2, close)) else null
        return if (target == null) null else Tagged(target.dest, target.title, close + 1)
    }

    private fun flush(buf: StringBuilder, out: MutableList<Inline>) {
        if (buf.isEmpty()) return
        out.add(Inline.Text(buf.toString()))
        buf.setLength(0)
    }

    /** 硬换行：行尾两个空格 → `br`；否则软换行按 CommonMark 归一为空格。 */
    private fun flushBreak(buf: StringBuilder, out: MutableList<Inline>) {
        val hard = buf.length >= 2 && buf[buf.length - 1] == ' ' && buf[buf.length - 2] == ' '
        if (hard) {
            while (buf.isNotEmpty() && buf.last() == ' ') buf.setLength(buf.length - 1)
        }
        flush(buf, out)
        out.add(if (hard) Inline.Br else Inline.Text(" "))
    }

    private fun merge(inlines: List<Inline>): List<Inline> {
        val out = ArrayList<Inline>(inlines.size)
        for (inline in inlines) {
            val last = out.lastOrNull()
            if (inline is Inline.Text && last is Inline.Text) {
                out[out.lastIndex] = Inline.Text(last.text + inline.text)
            } else if (inline !is Inline.Text || inline.text.isNotEmpty()) {
                out.add(inline)
            }
        }
        return out
    }

    private class Match(val value: Inline, val next: Int)

    private class Tagged(val dest: String, val title: String?, val end: Int)

    private companion object {
        private const val ASCII_PUNCTUATION: String = "\\`*_{}[]()#+-.!|>~$"
    }
}
