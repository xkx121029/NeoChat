package com.neochat.protocol.request

import com.neochat.protocol.Json
import com.neochat.protocol.JsonValue
import com.neochat.protocol.asObjOrNull
import com.neochat.protocol.field
import com.neochat.protocol.stringOrNull

/** 已序列化的一次 HTTP 请求（请求快照，重试复用同一份字节）。 */
public data class HttpRequestSpec(
    public val url: String,
    public val headers: Map<String, String>,
    public val body: ByteArray,
) {

    public fun header(name: String): String? =
        headers.entries.firstOrNull { it.key.equals(name, ignoreCase = true) }?.value
}

public data class OpenAiChatProvider(
    public val baseUrl: String,
    public val apiKey: String,
    public val orgId: String? = null,
    public val extraHeaders: Map<String, String> = emptyMap(),
    public val extraQuery: Map<String, String> = emptyMap(),
    public val extraBodyJson: String? = null,
    public val maxTokensField: String = "max_tokens",
)

/** providers.md §3.2：OpenAI Chat Completions 请求构造（含自定义 base_url 与 header）。 */
public object OpenAiChatRequestBuilder {

    private const val CHAT_COMPLETIONS_SUFFIX: String = "/chat/completions"
    private const val MAX_COMPLETION_TOKENS: String = "max_completion_tokens"
    private val AUTH_HEADER_NAMES: List<String> = listOf("authorization", "x-api-key", "api-key")

    public fun endpoint(provider: OpenAiChatProvider): String {
        val trimmed = provider.baseUrl.trimEnd('/')
        val base = if (trimmed.endsWith(CHAT_COMPLETIONS_SUFFIX)) trimmed else trimmed + CHAT_COMPLETIONS_SUFFIX
        if (provider.extraQuery.isEmpty()) return base
        val query = provider.extraQuery.entries
            .sortedBy { it.key }
            .joinToString("&") { (key, value) -> "$key=$value" }
        return "$base?$query"
    }

    public fun headers(provider: OpenAiChatProvider): Map<String, String> {
        val headers = LinkedHashMap<String, String>()
        headers["Content-Type"] = "application/json"
        headers["Accept"] = "text/event-stream"
        val custom = provider.extraHeaders
        val overridesAuth = custom.keys.any { name -> AUTH_HEADER_NAMES.any { it.equals(name, ignoreCase = true) } }
        if (!overridesAuth) headers["Authorization"] = "Bearer ${provider.apiKey}"
        provider.orgId?.let { headers["OpenAI-Organization"] = it }
        custom.forEach { (key, value) -> headers[key] = value }
        return headers
    }

    public fun build(
        request: ChatRequest,
        provider: OpenAiChatProvider,
        attachment: (String) -> AttachmentBytes = { error("unexpected image part") },
    ): HttpRequestSpec = HttpRequestSpec(
        url = endpoint(provider),
        headers = headers(provider),
        body = Json.write(body(request, provider, attachment)).toByteArray(Charsets.UTF_8),
    )

    public fun body(
        request: ChatRequest,
        provider: OpenAiChatProvider,
        attachment: (String) -> AttachmentBytes = { error("unexpected image part") },
    ): JsonValue.Obj {
        val fields = LinkedHashMap<String, JsonValue>()
        fields["model"] = JsonValue.Str(request.model)
        fields["messages"] = JsonValue.Arr(messages(request, attachment))
        fields["stream"] = JsonValue.Bool(true)
        fields["stream_options"] = Json.obj("include_usage" to JsonValue.Bool(true))
        if (request.tools.isNotEmpty()) fields["tools"] = JsonValue.Arr(request.tools.map { toolJson(it) })
        request.toolChoice?.let { fields["tool_choice"] = toolChoiceJson(it) }
        putSampling(fields, request.sampling, provider)
        if (request.reasoning?.mode == "effort") {
            request.reasoning.effort?.let { fields["reasoning_effort"] = JsonValue.Str(it) }
        }
        request.responseFormat?.let { fields["response_format"] = responseFormatJson(it) }
        request.clientRequestId?.let { fields["client_request_id"] = JsonValue.Str(it) }
        mergeExtraBody(fields, provider.extraBodyJson)
        return JsonValue.Obj(fields)
    }

    private fun messages(request: ChatRequest, attachment: (String) -> AttachmentBytes): List<JsonValue> {
        val list = ArrayList<JsonValue>()
        request.system?.let { list.add(Json.obj("role" to JsonValue.Str("system"), "content" to JsonValue.Str(it))) }
        request.messages.forEach { message -> list.add(messageJson(message, attachment)) }
        return list
    }

    private fun messageJson(message: ChatMessage, attachment: (String) -> AttachmentBytes): JsonValue {
        val fields = LinkedHashMap<String, JsonValue>()
        fields["role"] = JsonValue.Str(message.role.wire)
        when (message.role) {
            ChatRole.TOOL -> {
                message.toolCallId?.let { fields["tool_call_id"] = JsonValue.Str(it) }
                fields["content"] = JsonValue.Str(toolResultText(message.parts))
            }
            ChatRole.ASSISTANT -> assistantJson(message, fields)
            else -> contentJson(message.parts, attachment)?.let { fields["content"] = it }
        }
        return JsonValue.Obj(fields)
    }

    private fun assistantJson(message: ChatMessage, fields: MutableMap<String, JsonValue>) {
        val toolCalls = message.parts.filterIsInstance<MessagePart.ToolCall>()
        if (toolCalls.isNotEmpty()) fields["tool_calls"] = JsonValue.Arr(toolCalls.map { toolCallJson(it) })
        val text = message.parts.filterIsInstance<MessagePart.Text>().joinToString("") { it.text }
        if (text.isNotEmpty()) fields["content"] = JsonValue.Str(text)
    }

    private fun contentJson(parts: List<MessagePart>, attachment: (String) -> AttachmentBytes): JsonValue? {
        if (parts.isEmpty()) return null
        if (parts.all { it is MessagePart.Text }) {
            return JsonValue.Str(parts.filterIsInstance<MessagePart.Text>().joinToString("") { it.text })
        }
        return JsonValue.Arr(parts.mapNotNull { partJson(it, attachment) })
    }

    private fun partJson(part: MessagePart, attachment: (String) -> AttachmentBytes): JsonValue? = when (part) {
        is MessagePart.Text -> if (part.text.isEmpty()) {
            null
        } else {
            Json.obj("type" to JsonValue.Str("text"), "text" to JsonValue.Str(part.text))
        }
        is MessagePart.Image -> {
            val bytes = attachment(part.dataRef)
            Json.obj(
                "type" to JsonValue.Str("image_url"),
                "image_url" to Json.obj(
                    "url" to JsonValue.Str("data:${bytes.mime};base64,${bytes.base64}"),
                    "detail" to part.detail?.let { JsonValue.Str(it) },
                ),
            )
        }
        is MessagePart.ToolCall, is MessagePart.ToolResult -> null
    }

    private fun toolResultText(parts: List<MessagePart>): String {
        val results = parts.filterIsInstance<MessagePart.ToolResult>()
        if (results.isEmpty()) return ""
        if (results.size == 1) return wrapResult(results.first())
        return Json.write(JsonValue.Arr(results.map { wrapResultJson(it) }))
    }

    private fun wrapResult(result: MessagePart.ToolResult): String =
        Json.write(wrapResultJson(result))

    private fun wrapResultJson(result: MessagePart.ToolResult): JsonValue {
        val parsed = runCatching { Json.parse(result.resultJson) }.getOrNull()
            ?: JsonValue.Str(result.resultJson)
        return Json.obj("ok" to JsonValue.Bool(result.ok), "result" to parsed)
    }

    private fun toolCallJson(call: MessagePart.ToolCall): JsonValue = Json.obj(
        "id" to JsonValue.Str(call.id),
        "type" to JsonValue.Str("function"),
        "function" to Json.obj(
            "name" to JsonValue.Str(call.name),
            "arguments" to JsonValue.Str(call.argsJson),
        ),
    )

    private fun toolJson(tool: ToolSpec): JsonValue = Json.obj(
        "type" to JsonValue.Str("function"),
        "function" to Json.obj(
            "name" to JsonValue.Str(tool.name),
            "description" to JsonValue.Str(tool.description),
            "parameters" to parseJson(tool.parametersJson),
        ),
    )

    private fun toolChoiceJson(choice: ToolChoice): JsonValue = when (choice) {
        ToolChoice.Auto -> JsonValue.Str("auto")
        ToolChoice.None -> JsonValue.Str("none")
        ToolChoice.Required -> JsonValue.Str("required")
        is ToolChoice.Named -> Json.obj(
            "type" to JsonValue.Str("function"),
            "function" to Json.obj("name" to JsonValue.Str(choice.name)),
        )
    }

    private fun putSampling(fields: MutableMap<String, JsonValue>, sampling: Sampling, provider: OpenAiChatProvider) {
        sampling.temperature?.let { fields["temperature"] = JsonValue.Num(it.toString()) }
        sampling.topP?.let { fields["top_p"] = JsonValue.Num(it.toString()) }
        sampling.maxOutputTokens?.let {
            val field = if (provider.maxTokensField == MAX_COMPLETION_TOKENS) MAX_COMPLETION_TOKENS else "max_tokens"
            fields[field] = JsonValue.Num(it.toString())
        }
        sampling.stop?.let { fields["stop"] = JsonValue.Arr(it.map { value -> JsonValue.Str(value) }) }
        sampling.seed?.let { fields["seed"] = JsonValue.Num(it.toString()) }
        sampling.frequencyPenalty?.let { fields["frequency_penalty"] = JsonValue.Num(it.toString()) }
        sampling.presencePenalty?.let { fields["presence_penalty"] = JsonValue.Num(it.toString()) }
    }

    private fun responseFormatJson(format: ResponseFormat): JsonValue = when (format) {
        ResponseFormat.Text -> Json.obj("type" to JsonValue.Str("text"))
        ResponseFormat.JsonObject -> Json.obj("type" to JsonValue.Str("json_object"))
        is ResponseFormat.JsonSchema -> Json.obj(
            "type" to JsonValue.Str("json_schema"),
            "json_schema" to Json.obj(
                "name" to JsonValue.Str(format.name),
                "schema" to parseJson(format.schemaJson),
                "strict" to JsonValue.Bool(true),
            ),
        )
    }

    private fun mergeExtraBody(fields: MutableMap<String, JsonValue>, extraBodyJson: String?) {
        val extra = extraBodyJson ?: return
        val parsed = Json.parse(extra).asObjOrNull() ?: return
        parsed.fields.forEach { (key, value) -> fields[key] = value }
    }

    private fun parseJson(text: String): JsonValue =
        runCatching { Json.parse(text) }.getOrElse { JsonValue.Str(text) }

    /** provider 返回的兼容性提示字段（测试与降级重试共用）。 */
    public fun messageOf(body: String): String? =
        runCatching { Json.parse(body).field("error")?.field("message")?.stringOrNull() }.getOrNull()
}
