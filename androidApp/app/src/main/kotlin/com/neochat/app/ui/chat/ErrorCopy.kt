package com.neochat.app.ui.chat

import android.annotation.SuppressLint
import android.content.Context
import com.neochat.protocol.JsonValue
import com.neochat.protocol.errors.ErrorAction
import com.neochat.protocol.errors.ErrorObject

/**
 * 把错误对象里的文案键翻译成人话（spec/errors.md §6）。
 *
 * 查找顺序：`errors.<CODE>.title` / `errors.<CODE>.cause.<slug>` / `errors.<CODE>.action.<a>`，
 * 缺失则回落到共享键 `errors.cause.<slug>` / `errors.action.<a>`。
 * 一条都查不到时把键原样显示 —— 宁可露出键名，也不静默换成"未知错误"。
 */
public object ErrorCopy {

    private const val MISSING: Int = 0

    @SuppressLint("DiscouragedApi")
    private fun find(context: Context, key: String): Int =
        context.resources.getIdentifier(key.replace('.', '_'), "string", context.packageName)

    private fun resolve(context: Context, candidates: List<String>): String? {
        for (key in candidates) {
            val id = find(context, key)
            if (id != MISSING) return context.getString(id)
        }
        return null
    }

    /** 标题：`title_key` → 存量文案，参数用 `{name}` 占位。 */
    public fun title(context: Context, error: ErrorObject): String {
        val raw = resolve(context, listOf(error.titleKey)) ?: error.code.wire
        return substitute(raw, error)
    }

    /** 候选原因（≤3 条，按可能性降序）。 */
    public fun causes(context: Context, error: ErrorObject): List<String> = error.causes.mapNotNull { key ->
        resolve(context, listOf(key, "errors.cause.${key.substringAfterLast('.')}"))
    }

    /** 操作按钮文案。 */
    public fun actionLabel(context: Context, code: ErrorObject, action: ErrorAction): String =
        resolve(context, listOf("errors.${code.code.wire}.action.${action.a}", "errors.action.${action.a}"))
            ?: action.a

    /** action 自带的参数（`focus` 等），统一转成字符串给界面用。 */
    public fun params(action: ErrorAction): Map<String, String> = action.params.mapValues { jsonText(it.value) }

    private fun substitute(template: String, error: ErrorObject): String {
        var out = template
        error.params.forEach { (name, value) -> out = out.replace("{$name}", jsonText(value)) }
        error.httpStatus?.let { out = out.replace("{http_status}", it.toString()) }
        return out
    }

    private fun jsonText(value: JsonValue): String = when (value) {
        is JsonValue.Str -> value.value
        is JsonValue.Num -> value.raw
        is JsonValue.Bool -> value.value.toString()
        is JsonValue.Arr -> value.items.joinToString(separator = ", ") { jsonText(it) }
        is JsonValue.Obj -> value.fields.entries.joinToString(separator = ", ") { "${it.key}=${jsonText(it.value)}" }
        JsonValue.Null -> ""
    }
}
