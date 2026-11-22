package com.neochat.app.render

/** 代码围栏 info string 的解析结果（§2.1）。 */
internal data class CodeFenceInfo(val lang: String?, val meta: CodeMeta)

/**
 * §2.1：
 * `infostring := lang (SP attr)*`，`attr := "filename=" value | "ln" | "line_numbers"`。
 * 第一个 token 是 `lang`（可为空）；识别不出的 attr 一律忽略；整串无法切出 token 时整串当 `lang`。
 */
internal fun parseCodeFenceInfo(info: String): CodeFenceInfo {
    val trimmed = info.trim()
    if (trimmed.isEmpty()) return CodeFenceInfo(null, CodeMeta())
    val split = trimmed.indexOfFirst { it.isWhitespace() }
    if (split < 0) return CodeFenceInfo(trimmed, CodeMeta())
    val lang = trimmed.substring(0, split)
    val attrs = trimmed.substring(split + 1)
    val filename = FILENAME.find(attrs)?.let { match ->
        match.groups[1]?.value ?: match.groups[2]?.value
    }
    val lineNumbers = LINE_NUMBERS.containsMatchIn(attrs)
    return CodeFenceInfo(lang.ifEmpty { null }, CodeMeta(filename, lineNumbers))
}

/** mermaid 围栏的 `kind`（§2）：只认 flowchart 与 sequence，其余 `OTHER`。 */
internal fun mermaidKindOf(src: String): MermaidKind {
    val head = src.trimStart().takeWhile { !it.isWhitespace() }
    return when (head) {
        "graph", "flowchart" -> MermaidKind.FLOWCHART
        "sequenceDiagram" -> MermaidKind.SEQUENCE
        else -> MermaidKind.OTHER
    }
}

private val FILENAME: Regex = Regex("filename=(?:\"([^\"]*)\"|(\\S+))")
private val LINE_NUMBERS: Regex = Regex("(^|\\s)(ln|line_numbers)(\\s|$)")
