package com.neochat.protocol.errors

import java.nio.charset.StandardCharsets

/**
 * 脱敏管线（errors.md §7）。顺序固定：结构字段 → key 形态串 → 邮箱与本地路径 → UTF-8 边界截断。
 * 顺序颠倒会在截断边界留下半个 key。
 */
public object Redactor {

    public const val MAX_RAW_BYTES: Int = 512

    private val HEADER_FIELD = Regex(
        "(?i)\\b(authorization|x-api-key|api-key|cookie|set-cookie)\\s*[:=]\\s*[^\\r\\n]*",
    )
    private val QUERY_FIELD = Regex("(?i)([?&](?:key|api_key|access_token)=)[^&\\s\"'#]*")
    private val KEY_PATTERNS = listOf(
        Regex("sk-ant-[A-Za-z0-9_-]{16,}"),
        Regex("sk-[A-Za-z0-9_-]{16,}"),
        Regex("gsk_[A-Za-z0-9]{20,}"),
        Regex("xai-[A-Za-z0-9]{16,}"),
        Regex("AIza[0-9A-Za-z_-]{20,}"),
    )
    private val EMAIL = Regex("[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}")
    private val WINDOWS_HOME = Regex("([A-Za-z]:\\\\Users\\\\)[^\\\\\\s\"']+")
    private val POSIX_HOME = Regex("(/home/|/Users/)[^/\\s\"']+")

    public fun redact(input: String): String {
        var text = redactStructuralFields(input)
        text = redactKeyPatterns(text)
        text = redactEmails(text)
        text = redactHomePaths(text)
        return truncateUtf8(text, MAX_RAW_BYTES)
    }

    private fun redactStructuralFields(input: String): String {
        val headers = HEADER_FIELD.replace(input) { match -> "${match.groupValues[1]}: [redacted]" }
        return QUERY_FIELD.replace(headers) { match -> "${match.groupValues[1]}[redacted]" }
    }

    private fun redactKeyPatterns(input: String): String {
        var text = input
        for (pattern in KEY_PATTERNS) {
            text = pattern.replace(text) { match -> maskKey(match.value) }
        }
        return text
    }

    private fun maskKey(key: String): String =
        if (key.length <= 8) "[redacted]" else key.take(4) + "\u2026" + key.takeLast(4)

    private fun redactEmails(input: String): String = EMAIL.replace(input) { match ->
        val value = match.value
        val at = value.indexOf('@')
        val local = value.substring(0, at)
        val domain = value.substring(at + 1)
        local.take(1) + "***" + domain
    }

    private fun redactHomePaths(input: String): String {
        val windows = WINDOWS_HOME.replace(input) { match -> match.groupValues[1] + "~" }
        return POSIX_HOME.replace(windows) { match -> match.groupValues[1] + "~" }
    }

    private fun truncateUtf8(input: String, maxBytes: Int): String {
        val encoded = input.toByteArray(StandardCharsets.UTF_8)
        if (encoded.size <= maxBytes) return input
        var bytes = 0
        val out = StringBuilder()
        for (ch in input) {
            val cost = when {
                ch.isHighSurrogate() -> 4
                ch.isLowSurrogate() -> 0
                ch.code < 0x80 -> 1
                ch.code < 0x800 -> 2
                else -> 3
            }
            if (bytes + cost > maxBytes) break
            bytes += cost
            out.append(ch)
        }
        return out.toString()
    }
}
