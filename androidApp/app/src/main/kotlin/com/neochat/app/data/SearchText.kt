package com.neochat.app.data

/**
 * `message_fts` 的写入与查询共用同一套分词。ADR-004 尚未定案，这里先按临时口径实现：
 *
 * - 拉丁字母与数字：交给 FTS5 的 `unicode61` 原样切词（不另做大小写或词形处理）；
 * - CJK（汉字 / 假名 / 谚文）：写入前切成覆盖全部相邻两字的二元组（bigram），
 *   查询侧用同一函数把查询串也切成二元组并拼成短语，所以"同输入同分词"。
 *
 * 二元组的选择原因：中文里两字词（`总结`、`文章`）是最常见的查询粒度，
 * unicode61 会把整段中文当一个词，导致两字查询零命中。
 */
public object SearchText {

    /** 写进 `message_fts.text` 的形态：以空格分隔的分词串，空输入返回空串。 */
    public fun tokens(text: String): String = runs(text)
        .flatMap { run -> if (run.cjk) bigrams(run.text) else listOf(run.text) }
        .joinToString(" ")

    /**
     * 查询串 → FTS5 `MATCH` 表达式。每段 CJK 拼成一个短语（bigram 必须按顺序相邻），
     * 片段之间是隐式 AND；没有可搜索字符时返回 null。
     */
    public fun matchExpression(query: String): String? {
        val parts = runs(query).map { run ->
            if (run.cjk) quote(bigrams(run.text).joinToString(" ")) else quote(run.text)
        }
        return parts.takeIf { it.isNotEmpty() }?.joinToString(" ")
    }

    private data class Run(val cjk: Boolean, val text: String)

    private fun runs(text: String): List<Run> {
        val out = mutableListOf<Run>()
        val buffer = StringBuilder()
        var cjk = false
        var index = 0
        while (index < text.length) {
            val codePoint = text.codePointAt(index)
            val isCjk = isCjk(codePoint)
            val searchable = isCjk || isWord(codePoint)
            if (!searchable || (buffer.isNotEmpty() && isCjk != cjk)) {
                if (buffer.isNotEmpty()) out += Run(cjk, buffer.toString())
                buffer.setLength(0)
            }
            if (searchable) {
                buffer.appendCodePoint(codePoint)
                cjk = isCjk
            }
            index += Character.charCount(codePoint)
        }
        if (buffer.isNotEmpty()) out += Run(cjk, buffer.toString())
        return out
    }

    private fun bigrams(text: String): List<String> {
        val points = text.codePoints().toArray()
        if (points.size < 2) return listOf(text)
        return (0 until points.size - 1).map { at -> String(points, at, 2) }
    }

    private fun quote(token: String): String = "\"" + token.replace("\"", "\"\"") + "\""

    private fun isCjk(codePoint: Int): Boolean = codePoint in 0x2E80..0x2EFF ||
        codePoint in 0x3005..0x3007 ||
        codePoint in 0x3040..0x30FF ||
        codePoint in 0x3400..0x4DBF ||
        codePoint in 0x4E00..0x9FFF ||
        codePoint in 0xF900..0xFAFF ||
        codePoint in 0xAC00..0xD7AF ||
        codePoint in 0x1100..0x11FF ||
        codePoint in 0x20000..0x2FA1F

    private fun isWord(codePoint: Int): Boolean =
        Character.isLetterOrDigit(codePoint) || codePoint == '_'.code
}
