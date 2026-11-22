package com.neochat.protocol.errors

import com.neochat.protocol.JsonValue
import java.security.SecureRandom

/** errors.md §1 阶段闭集。 */
public enum class ErrorStage(public val wire: String) {
    CONFIG("config"),
    RESOLVE("resolve"),
    CONNECT("connect"),
    SEND("send"),
    FIRST_BYTE("first_byte"),
    STREAM_READ("stream_read"),
    PARSE("parse"),
    PROVIDER("provider"),
    TOOL("tool"),
    PERSIST("persist"),
    CRYPTO("crypto"),
    LOCAL("local"),
}

/** errors.md §3 中本里程碑可达的错误码（Anthropic/Gemini 专属分支由 M2 补齐）。 */
public enum class ErrorCode(public val wire: String) {
    AUTH_INVALID_KEY("AUTH_INVALID_KEY"),
    AUTH_INSUFFICIENT_QUOTA("AUTH_INSUFFICIENT_QUOTA"),
    RATE_LIMITED("RATE_LIMITED"),
    MODEL_NOT_FOUND("MODEL_NOT_FOUND"),
    PARAM_INVALID("PARAM_INVALID"),
    CONTEXT_OVERFLOW("CONTEXT_OVERFLOW"),
    CONTENT_FILTERED("CONTENT_FILTERED"),
    SERVER_ERROR("SERVER_ERROR"),
    SERVER_OVERLOADED("SERVER_OVERLOADED"),
    ENDPOINT_NOT_FOUND("ENDPOINT_NOT_FOUND"),
    UNKNOWN_PROVIDER_ERROR("UNKNOWN_PROVIDER_ERROR"),
    NET_CONNECTION_RESET("NET_CONNECTION_RESET"),
    NET_READ_TIMEOUT("NET_READ_TIMEOUT"),
    FIRST_BYTE_TIMEOUT("FIRST_BYTE_TIMEOUT"),
    PARSE_SSE_INVALID("PARSE_SSE_INVALID"),
    PARSE_INCOMPLETE("PARSE_INCOMPLETE"),
    PARSE_EVENT_TOO_LARGE("PARSE_EVENT_TOO_LARGE"),
    INTERNAL("INTERNAL"),
}

/** errors.md §4 操作：`a` 加上该操作自己的参数。 */
public data class ErrorAction(public val a: String, public val params: Map<String, JsonValue> = emptyMap()) {

    public fun toJson(): JsonValue {
        val fields = LinkedHashMap<String, JsonValue>()
        fields["a"] = JsonValue.Str(a)
        params.forEach { (key, value) -> fields[key] = value }
        return JsonValue.Obj(fields)
    }
}

/** provider 侧事实，决定 `top_up` / `view_service_status` 这类依赖配置的 action 是否出现。 */
public data class ProviderFacts(
    public val provider: String? = null,
    public val model: String? = null,
    public val billingUrl: String? = null,
    public val statusUrl: String? = null,
)

/** 线格式即 StreamEvent.failed.error（errors.md §2）。 */
public data class ErrorObject(
    public val code: ErrorCode,
    public val stage: ErrorStage,
    public val retryable: Boolean,
    public val titleKey: String,
    public val causes: List<String>,
    public val actions: List<ErrorAction>,
    public val traceId: String,
    public val params: Map<String, JsonValue> = emptyMap(),
    public val provider: String? = null,
    public val model: String? = null,
    public val httpStatus: Int? = null,
    public val providerCode: String? = null,
    public val retryAfterMs: Long? = null,
    public val raw: String? = null,
    public val requestId: String? = null,
) {

    public fun toJson(): JsonValue.Obj {
        val fields = LinkedHashMap<String, JsonValue>()
        fields["code"] = JsonValue.Str(code.wire)
        fields["stage"] = JsonValue.Str(stage.wire)
        provider?.let { fields["provider"] = JsonValue.Str(it) }
        model?.let { fields["model"] = JsonValue.Str(it) }
        httpStatus?.let { fields["http_status"] = JsonValue.Num(it.toString()) }
        providerCode?.let { fields["provider_code"] = JsonValue.Str(it) }
        fields["retryable"] = JsonValue.Bool(retryable)
        retryAfterMs?.let { fields["retry_after_ms"] = JsonValue.Num(it.toString()) }
        fields["title_key"] = JsonValue.Str(titleKey)
        fields["params"] = JsonValue.Obj(params.toSortedMap())
        fields["causes"] = JsonValue.Arr(causes.map { JsonValue.Str(it) })
        fields["actions"] = JsonValue.Arr(actions.map { it.toJson() })
        raw?.let { fields["raw"] = JsonValue.Str(it) }
        requestId?.let { fields["request_id"] = JsonValue.Str(it) }
        fields["trace_id"] = JsonValue.Str(traceId)
        return JsonValue.Obj(fields)
    }
}

/** errors.md §3 的 code → (stage, retryable, causes, actions) 映射表。 */
public object ErrorCatalog {

    public data class Definition(
        public val stage: ErrorStage,
        public val retryable: Boolean,
        public val causes: List<String>,
        public val actions: List<ErrorAction>,
    )

    public fun define(code: ErrorCode, facts: ProviderFacts): Definition {
        val prefix = "errors.${code.wire}"
        val causes = { slugs: List<String> -> slugs.map { "$prefix.cause.$it" } }
        return when (code) {
            ErrorCode.AUTH_INVALID_KEY -> Definition(
                stage = ErrorStage.PROVIDER,
                retryable = false,
                causes = causes(listOf("revoked", "wrong_endpoint", "truncated_paste")),
                actions = listOf(
                    ErrorAction("edit_provider", mapOf("focus" to JsonValue.Str("key"))),
                    ErrorAction("check_base_url"),
                    ErrorAction("copy_diagnostics"),
                ),
            )
            ErrorCode.AUTH_INSUFFICIENT_QUOTA -> Definition(
                stage = ErrorStage.PROVIDER,
                retryable = false,
                causes = causes(listOf("balance", "free_tier", "org_quota")),
                actions = buildList {
                    if (facts.billingUrl != null) add(ErrorAction("top_up"))
                    add(ErrorAction("switch_model"))
                    add(ErrorAction("manage_keys"))
                },
            )
            ErrorCode.RATE_LIMITED -> Definition(
                stage = ErrorStage.PROVIDER,
                retryable = true,
                causes = causes(listOf("rpm", "concurrency", "daily_quota")),
                actions = listOf(
                    ErrorAction("retry_after", mapOf("ms" to JsonValue.Num("0"))),
                    ErrorAction("edit_provider"),
                    ErrorAction("manage_keys"),
                ),
            )
            ErrorCode.MODEL_NOT_FOUND -> Definition(
                stage = ErrorStage.PROVIDER,
                retryable = false,
                causes = causes(listOf("retired", "typo", "endpoint_lacks_model")),
                actions = listOf(ErrorAction("refresh_models"), ErrorAction("edit_provider")),
            )
            ErrorCode.PARAM_INVALID -> Definition(
                stage = ErrorStage.PROVIDER,
                retryable = false,
                causes = causes(listOf("unsupported", "out_of_range", "mixed")),
                actions = listOf(ErrorAction("view_raw"), ErrorAction("edit_provider"), ErrorAction("switch_model")),
            )
            ErrorCode.CONTEXT_OVERFLOW -> Definition(
                stage = ErrorStage.PROVIDER,
                retryable = false,
                causes = causes(listOf("history", "attachment", "window")),
                actions = listOf(
                    ErrorAction("reduce_context"),
                    ErrorAction("switch_model"),
                    ErrorAction("edit_provider"),
                ),
            )
            ErrorCode.CONTENT_FILTERED -> Definition(
                stage = ErrorStage.PROVIDER,
                retryable = false,
                causes = causes(listOf("input", "output", "region")),
                actions = listOf(ErrorAction("retry"), ErrorAction("switch_model")),
            )
            ErrorCode.SERVER_ERROR -> Definition(
                stage = ErrorStage.PROVIDER,
                retryable = true,
                causes = causes(listOf("fault", "restart", "region")),
                actions = buildList {
                    add(ErrorAction("retry"))
                    add(ErrorAction("switch_model"))
                    if (facts.statusUrl != null) add(ErrorAction("view_service_status"))
                },
            )
            ErrorCode.SERVER_OVERLOADED -> Definition(
                stage = ErrorStage.PROVIDER,
                retryable = true,
                causes = causes(listOf("queue", "degraded", "capacity")),
                actions = listOf(
                    ErrorAction("retry_after", mapOf("ms" to JsonValue.Num("0"))),
                    ErrorAction("switch_model"),
                ),
            )
            ErrorCode.ENDPOINT_NOT_FOUND -> Definition(
                stage = ErrorStage.PROVIDER,
                retryable = false,
                causes = causes(listOf("v1", "protocol", "path")),
                actions = listOf(
                    ErrorAction("check_base_url"),
                    ErrorAction("edit_provider"),
                    ErrorAction("open_docs", mapOf("topic" to JsonValue.Str("base_url"))),
                ),
            )
            ErrorCode.UNKNOWN_PROVIDER_ERROR -> Definition(
                stage = ErrorStage.PROVIDER,
                retryable = false,
                causes = causes(listOf("format", "proxy", "relay")),
                actions = listOf(ErrorAction("view_raw"), ErrorAction("copy_diagnostics"), ErrorAction("switch_model")),
            )
            ErrorCode.NET_CONNECTION_RESET -> Definition(
                stage = ErrorStage.STREAM_READ,
                retryable = true,
                causes = causes(listOf("switch", "proxy", "server")),
                actions = listOf(ErrorAction("retry"), ErrorAction("check_proxy")),
            )
            ErrorCode.NET_READ_TIMEOUT -> Definition(
                stage = ErrorStage.STREAM_READ,
                retryable = true,
                causes = causes(listOf("queue", "buffer", "large")),
                actions = listOf(ErrorAction("retry"), ErrorAction("switch_model"), ErrorAction("check_proxy")),
            )
            ErrorCode.FIRST_BYTE_TIMEOUT -> Definition(
                stage = ErrorStage.FIRST_BYTE,
                retryable = true,
                causes = causes(listOf("queue", "buffer", "large")),
                actions = listOf(ErrorAction("retry"), ErrorAction("switch_model"), ErrorAction("check_proxy")),
            )
            ErrorCode.PARSE_SSE_INVALID -> Definition(
                stage = ErrorStage.PARSE,
                retryable = false,
                causes = causes(listOf("non_stream", "html_page", "json_error")),
                actions = listOf(ErrorAction("check_base_url"), ErrorAction("check_proxy"), ErrorAction("view_raw")),
            )
            ErrorCode.PARSE_INCOMPLETE -> Definition(
                stage = ErrorStage.PARSE,
                retryable = true,
                causes = causes(listOf("network", "server", "proxy")),
                actions = listOf(ErrorAction("retry"), ErrorAction("check_proxy")),
            )
            ErrorCode.PARSE_EVENT_TOO_LARGE -> Definition(
                stage = ErrorStage.PARSE,
                retryable = false,
                causes = causes(listOf("huge", "duplicated")),
                actions = listOf(ErrorAction("check_base_url"), ErrorAction("view_raw")),
            )
            ErrorCode.INTERNAL -> Definition(
                stage = ErrorStage.LOCAL,
                retryable = false,
                causes = causes(listOf("defect", "state")),
                actions = listOf(ErrorAction("copy_diagnostics"), ErrorAction("retry")),
            )
        }
    }
}

/** trace_id：UUIDv7（时间有序），非确定性字段，不参与 conformance 字节比对。 */
public object TraceIds {

    private val random = SecureRandom()

    public fun newId(nowMs: Long = System.currentTimeMillis()): String {
        val bytes = ByteArray(16)
        random.nextBytes(bytes)
        bytes[0] = ((nowMs ushr 40) and 0xFF).toByte()
        bytes[1] = ((nowMs ushr 32) and 0xFF).toByte()
        bytes[2] = ((nowMs ushr 24) and 0xFF).toByte()
        bytes[3] = ((nowMs ushr 16) and 0xFF).toByte()
        bytes[4] = ((nowMs ushr 8) and 0xFF).toByte()
        bytes[5] = (nowMs and 0xFF).toByte()
        bytes[6] = ((bytes[6].toInt() and 0x0F) or 0x70).toByte()
        bytes[8] = ((bytes[8].toInt() and 0x3F) or 0x80).toByte()
        val hex = StringBuilder(36)
        for (i in 0 until 16) {
            if (i == 4 || i == 6 || i == 8 || i == 10) hex.append('-')
            hex.append(((bytes[i].toInt() shr 4) and 0x0F).toString(16))
            hex.append((bytes[i].toInt() and 0x0F).toString(16))
        }
        return hex.toString()
    }
}
