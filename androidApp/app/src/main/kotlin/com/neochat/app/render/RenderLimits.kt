package com.neochat.app.render

/** spec/render-model.md §5 的上限表；双端必须同值。 */
public data class RenderLimits(
    /** 总输入字符，超出截断并置 `truncated`。 */
    public val maxChars: Int = 200_000,
    /** 单消息块数。 */
    public val maxBlocks: Int = 5_000,
    /** 表格列数，超出列丢弃。 */
    public val maxTableColumns: Int = 60,
    /** 表格行数。 */
    public val maxTableRows: Int = 5_000,
    /** list / quote 嵌套深度，超出降级为 `unknown`。 */
    public val maxNesting: Int = 32,
    /** 内联嵌套深度，超出降级为 `text`。 */
    public val maxInlineDepth: Int = 8,
    /** 代码块行数，超出部分不渲染但保留 `text`。 */
    public val maxCodeLines: Int = 20_000,
) {
    public companion object {
        /** spec/render-model.md §5 的默认上限。 */
        public val Default: RenderLimits = RenderLimits()
    }
}

/** §5 总输入上限的截断（不切开代理对）。 */
internal fun clipSource(source: String, limits: RenderLimits): String {
    if (source.length <= limits.maxChars) return source
    val cut = source[limits.maxChars - 1]
    return if (cut.isHighSurrogate()) source.substring(0, limits.maxChars - 1) else source.substring(0, limits.maxChars)
}
