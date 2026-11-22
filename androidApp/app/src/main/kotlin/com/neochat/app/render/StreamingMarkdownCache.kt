package com.neochat.app.render

/**
 * §4.7 增量解析：只重解析尾部（最后一个块），其余块原样复用。
 * 追加式流用 [render]（同一份文本重复调用返回缓存模型）；前缀被改写时退回全文解析。
 */
public class StreamingMarkdownCache(private val parser: MarkdownParser = MarkdownParser()) {

    private var source: String = ""
    private var model: RenderModel = RenderModel()
    private var tailStart: Int = 0

    /** 当前文本的模型；[incomplete] 对应 `flags.incomplete`。 */
    public fun render(next: String, incomplete: Boolean): RenderModel {
        val clipped = clipSource(next, parser.limits)
        if (clipped == source && incomplete == model.flags.incomplete) return model
        val incremental = clipped.startsWith(source) && source.isNotEmpty() &&
            model.blocks.isNotEmpty() && tailStart <= clipped.length
        val from = if (incremental) tailStart else 0
        val idBase = if (incremental) model.blocks.size - 1 else 0
        val slice = parser.parseTail(clipped, from, idBase, incomplete)
        val blocks = if (idBase > 0) model.blocks.dropLast(1) + slice.blocks else slice.blocks
        val updated = parser.assemble(blocks, incomplete, slice.truncated)
        source = clipped
        model = updated
        tailStart = slice.starts.lastOrNull() ?: from
        return updated
    }
}

/** §7 安全规则：链接协议白名单、图片来源白名单。 */
public object RenderSafety {

    private val ALLOWED_LINK_SCHEMES: Set<String> = setOf("http", "https", "mailto")
    private val ALLOWED_IMAGE_SCHEMES: Set<String> = setOf("http", "https", "attachment")

    /** 只有 `http` / `https` / `mailto` 可点击；其余（`javascript:`、`data:`、`file:`…）按纯文本渲染。 */
    public fun isAllowedLinkTarget(dest: String): Boolean {
        val scheme = schemeOf(dest)
        return scheme != null && scheme in ALLOWED_LINK_SCHEMES
    }

    /** 图片只允许 `http(s)` 与本地附件引用 `attachment://<id>`。 */
    public fun isAllowedImageSource(src: String): Boolean {
        val scheme = schemeOf(src)
        return scheme != null && scheme in ALLOWED_IMAGE_SCHEMES
    }

    private fun schemeOf(uri: String): String? {
        val colon = uri.indexOf(':')
        if (colon <= 0) return null
        val scheme = uri.substring(0, colon)
        val valid = scheme.first().isLetter() && scheme.all { it.isLetterOrDigit() || it in "+-." }
        return if (valid) scheme.lowercase() else null
    }
}

/**
 * 渲染层需要的中文文案。本期没有新增 `res/values` 条目（超出任务允许的路径），
 * 这些字符串后续要挪进 `strings.xml` 并走 `stringResource`。
 */
public object RenderLabels {
    /** 公式块：本期只做等宽源码，闭合后不切换排版。 */
    public const val MATH: String = "LaTeX · 等宽源码（公式排版下期）"
    /** Mermaid 围栏：本期只显示源码。 */
    public const val MERMAID: String = "Mermaid · 源码（图形渲染下期）"
    /** 原始 HTML 块。 */
    public const val HTML: String = "HTML · 已转义为字面文本"
    /** 未知语法块。 */
    public const val UNKNOWN: String = "未识别语法"
    /** 代码块没有语言标识时的占位。 */
    public const val CODE: String = "代码"
    /** 表格/列表之外的长度提示（§1 `truncated`）。 */
    public const val TRUNCATED: String = "内容过长，尾部未渲染"
    /** 块级图片的占位说明。 */
    public const val IMAGE: String = "图片"
    /** 脚注定义标签前缀。 */
    public const val FOOTNOTE: String = "脚注"
    /** 不在白名单的链接。 */
    public const val LINK_BLOCKED: String = "（不可点击）"
}
