package com.neochat.protocol.errors

import com.neochat.protocol.Json
import com.neochat.protocol.JsonParseException
import com.neochat.protocol.JsonValue
import com.neochat.protocol.asObjOrNull
import com.neochat.protocol.field
import com.neochat.protocol.stringOrNull

/** provider 错误体的三要素（OpenAI 系：`error.code` / `error.type` / `error.message`）。 */
internal data class ProviderErrorInfo(
    val code: String?,
    val type: String?,
    val message: String?,
) {

    private val haystack: String = listOfNotNull(code, type, message).joinToString(" ").lowercase()

    fun mentions(vararg needles: String): Boolean = needles.any { haystack.contains(it) }

    fun mentionsQuota(): Boolean = mentions("insufficient_quota", "exceeded your current quota", "billing")

    companion object {

        fun from(body: String): ProviderErrorInfo {
            val root = runCatching { Json.parse(body) }.getOrNull() ?: return ProviderErrorInfo(null, null, null)
            val node = root.field("error")?.asObjOrNull() ?: root.asObjOrNull()
                ?: return ProviderErrorInfo(null, null, null)
            return ProviderErrorInfo(
                code = textOf(node.field("code")),
                type = textOf(node.field("type")),
                message = textOf(node.field("message")),
            )
        }

        private fun textOf(value: JsonValue?): String? = when (value) {
            null, JsonValue.Null -> null
            is JsonValue.Str -> value.value
            is JsonValue.Num -> value.raw
            is JsonValue.Bool -> value.value.toString()
            else -> null
        }
    }
}

/**
 * errors.md §5.1 的 OpenAI 系映射，外加 §2.3 的"HTTP 200 但 body 是 JSON 错误对象"。
 * 只做映射与脱敏，不做任何重试决策。
 */
public object ErrorMapper {

    private const val MAX_RETRY_AFTER_MS: Long = 30_000L
    private const val MAX_BODY_CHARS: Int = 65_536

    private val KEY_SHAPED = Regex("(sk-[A-Za-z0-9_-]{8,}|AIza[0-9A-Za-z_-]{8,}|gsk_[A-Za-z0-9]{8,}|xai-[A-Za-z0-9]{8,})")
    private val FIELD_NAME = Regex("(?:parameter|param|value)\\s*[:' ]*\\s*'([A-Za-z0-9_.\\[\\]-]+)'")

    public fun fromHttp(
        status: Int,
        headers: Map<String, String>,
        bodyText: String,
        facts: ProviderFacts = ProviderFacts(),
    ): ErrorObject {
        val body = bodyText.take(MAX_BODY_CHARS)
        val info = ProviderErrorInfo.from(body)
        val code = classify(status, info)
        val retryAfterMs = retryAfterMs(headers)
        val shouldRetry = header(headers, "x-should-retry")?.trim()?.lowercase() == "true"
        return build(
            code = code,
            facts = facts,
            httpStatus = status,
            providerCode = info.code ?: info.type,
            retryableOverride = if (shouldRetry) true else null,
            retryAfterMs = retryAfterMs,
            requestId = header(headers, "x-request-id") ?: header(headers, "request-id"),
            raw = bodyText,
            params = paramsFor(code, info, status),
        )
    }

    /** 流中途的 `data: {"error":{...}}`：无 HTTP 状态可用，按 body 分类。 */
    public fun fromStreamPayload(payload: String, facts: ProviderFacts = ProviderFacts()): ErrorObject =
        fromHttp(200, emptyMap(), payload, facts)

    public fun parseInvalid(facts: ProviderFacts, raw: String? = null): ErrorObject = build(
        code = ErrorCode.PARSE_SSE_INVALID,
        facts = facts,
        raw = raw,
    )

    public fun parseIncomplete(facts: ProviderFacts, partialTextLength: Int): ErrorObject = build(
        code = ErrorCode.PARSE_INCOMPLETE,
        facts = facts,
        params = mapOf("partial_text_length" to JsonValue.Num(partialTextLength.toString())),
    )

    public fun parseTooLarge(facts: ProviderFacts, limitBytes: Long): ErrorObject = build(
        code = ErrorCode.PARSE_EVENT_TOO_LARGE,
        facts = facts,
        params = mapOf("limit_bytes" to JsonValue.Str(limitBytes.toString())),
    )

    public fun connectionReset(facts: ProviderFacts, detail: String? = null): ErrorObject = build(
        code = ErrorCode.NET_CONNECTION_RESET,
        facts = facts,
        raw = detail,
    )

    public fun readTimeout(facts: ProviderFacts): ErrorObject = build(
        code = ErrorCode.NET_READ_TIMEOUT,
        facts = facts,
    )

    public fun firstByteTimeout(facts: ProviderFacts): ErrorObject = build(
        code = ErrorCode.FIRST_BYTE_TIMEOUT,
        facts = facts,
    )

    public fun internal(facts: ProviderFacts, detail: String): ErrorObject = build(
        code = ErrorCode.INTERNAL,
        facts = facts,
        raw = detail,
    )

    private fun classify(status: Int, info: ProviderErrorInfo): ErrorCode = when {
        status in 200..299 -> classifyBody(info) ?: ErrorCode.PARSE_SSE_INVALID
        status == 400 || status == 422 -> classifyBadRequest(info)
        status == 401 -> ErrorCode.AUTH_INVALID_KEY
        status == 402 -> ErrorCode.AUTH_INSUFFICIENT_QUOTA
        status == 403 -> if (info.mentions("country", "region")) ErrorCode.CONTENT_FILTERED else ErrorCode.AUTH_INVALID_KEY
        status == 404 -> if (info.mentions("model")) ErrorCode.MODEL_NOT_FOUND else ErrorCode.ENDPOINT_NOT_FOUND
        status == 413 -> ErrorCode.CONTEXT_OVERFLOW
        status == 429 -> if (info.mentionsQuota()) ErrorCode.AUTH_INSUFFICIENT_QUOTA else ErrorCode.RATE_LIMITED
        status == 500 || status == 502 || status == 503 || status == 504 -> ErrorCode.SERVER_ERROR
        status == 529 -> ErrorCode.SERVER_OVERLOADED
        else -> classifyBody(info) ?: ErrorCode.UNKNOWN_PROVIDER_ERROR
    }

    private fun classifyBadRequest(info: ProviderErrorInfo): ErrorCode = when {
        info.mentions("context_length_exceeded", "context length", "prompt is too long", "token count") ->
            ErrorCode.CONTEXT_OVERFLOW
        info.mentions("content_filter", "content_policy") -> ErrorCode.CONTENT_FILTERED
        else -> ErrorCode.PARAM_INVALID
    }

    private fun classifyBody(info: ProviderErrorInfo): ErrorCode? = when {
        info.mentionsQuota() -> ErrorCode.AUTH_INSUFFICIENT_QUOTA
        info.mentions("invalid_api_key", "incorrect api key", "authentication_error", "permission_error") ->
            ErrorCode.AUTH_INVALID_KEY
        info.mentions("context_length_exceeded", "maximum context length", "prompt is too long") ->
            ErrorCode.CONTEXT_OVERFLOW
        info.mentions("rate_limit", "rate limit") -> ErrorCode.RATE_LIMITED
        info.mentions("model_not_found", "does not exist") && info.mentions("model") -> ErrorCode.MODEL_NOT_FOUND
        info.mentions("content_filter", "content_policy") -> ErrorCode.CONTENT_FILTERED
        info.mentions("overloaded") -> ErrorCode.SERVER_OVERLOADED
        info.mentions("server_error", "internal error") -> ErrorCode.SERVER_ERROR
        else -> null
    }

    private fun paramsFor(code: ErrorCode, info: ProviderErrorInfo, status: Int): Map<String, JsonValue> {
        val message = info.message ?: ""
        return when (code) {
            ErrorCode.SERVER_ERROR -> mapOf("http_status" to JsonValue.Num(status.toString()))
            ErrorCode.PARAM_INVALID -> FIELD_NAME.find(message)
                ?.let { mapOf<String, JsonValue>("field" to JsonValue.Str(it.groupValues[1])) }
                ?: emptyMap()
            ErrorCode.AUTH_INVALID_KEY -> KEY_SHAPED.find(message)
                ?.let { mapOf<String, JsonValue>("hint" to JsonValue.Str(Redactor.redact(it.value))) }
                ?: emptyMap()
            else -> emptyMap()
        }
    }

    private fun build(
        code: ErrorCode,
        facts: ProviderFacts,
        httpStatus: Int? = null,
        providerCode: String? = null,
        retryableOverride: Boolean? = null,
        retryAfterMs: Long? = null,
        raw: String? = null,
        requestId: String? = null,
        params: Map<String, JsonValue> = emptyMap(),
    ): ErrorObject {
        val definition = ErrorCatalog.define(code, facts)
        val actions = definition.actions.map { action ->
            if (action.a == "retry_after") {
                ErrorAction(action.a, mapOf("ms" to JsonValue.Num((retryAfterMs ?: 0L).toString())))
            } else {
                action
            }
        }
        return ErrorObject(
            code = code,
            stage = definition.stage,
            retryable = retryableOverride ?: definition.retryable,
            titleKey = "errors.${code.wire}.title",
            causes = definition.causes,
            actions = actions,
            traceId = TraceIds.newId(),
            params = params,
            provider = facts.provider,
            model = facts.model,
            httpStatus = httpStatus,
            providerCode = providerCode,
            retryAfterMs = retryAfterMs,
            raw = raw?.let { Redactor.redact(it) },
            requestId = requestId,
        )
    }

    private fun header(headers: Map<String, String>, name: String): String? =
        headers.entries.firstOrNull { it.key.equals(name, ignoreCase = true) }?.value

    private fun retryAfterMs(headers: Map<String, String>): Long? {
        val raw = header(headers, "retry-after")?.trim() ?: return null
        val seconds = raw.toLongOrNull() ?: return null
        return (seconds * 1000L).coerceIn(0L, MAX_RETRY_AFTER_MS)
    }

    /** JSON 解析失败仅用于诊断，不影响映射主路径。 */
    internal fun isJsonObject(text: String): Boolean = try {
        Json.parse(text).asObjOrNull() != null
    } catch (e: JsonParseException) {
        false
    }
}
