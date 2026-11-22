package com.neochat.protocol.request

/** Canonical ChatRequest（providers.md §2，本次只覆盖 openai_chat 所需的字段）。 */
public data class ChatRequest(
    public val model: String,
    public val system: String? = null,
    public val messages: List<ChatMessage> = emptyList(),
    public val tools: List<ToolSpec> = emptyList(),
    public val toolChoice: ToolChoice? = null,
    public val sampling: Sampling = Sampling(),
    public val reasoning: Reasoning? = null,
    public val responseFormat: ResponseFormat? = null,
    public val clientRequestId: String? = null,
)

public enum class ChatRole(public val wire: String) {
    SYSTEM("system"),
    USER("user"),
    ASSISTANT("assistant"),
    TOOL("tool"),
}

public data class ChatMessage(
    public val role: ChatRole,
    public val parts: List<MessagePart> = emptyList(),
    public val toolCallId: String? = null,
)

public sealed interface MessagePart {

    public data class Text(public val text: String) : MessagePart

    public data class Image(public val mime: String, public val dataRef: String, public val detail: String? = null) :
        MessagePart

    public data class ToolCall(public val id: String, public val name: String, public val argsJson: String) :
        MessagePart

    public data class ToolResult(public val ok: Boolean, public val resultJson: String) : MessagePart
}

public data class ToolSpec(
    public val name: String,
    public val description: String,
    public val parametersJson: String,
)

public sealed interface ToolChoice {

    public data object Auto : ToolChoice

    public data object None : ToolChoice

    public data object Required : ToolChoice

    public data class Named(public val name: String) : ToolChoice
}

public data class Sampling(
    public val temperature: Double? = null,
    public val topP: Double? = null,
    public val maxOutputTokens: Int? = null,
    public val stop: List<String>? = null,
    public val seed: Long? = null,
    public val frequencyPenalty: Double? = null,
    public val presencePenalty: Double? = null,
)

/** `mode="off"` 时不发送任何推理参数。 */
public data class Reasoning(
    public val mode: String = "off",
    public val effort: String? = null,
    public val budgetTokens: Int? = null,
)

public sealed interface ResponseFormat {

    public data object Text : ResponseFormat

    public data object JsonObject : ResponseFormat

    public data class JsonSchema(public val name: String, public val schemaJson: String) : ResponseFormat
}

/** 附件解析结果：协议层只接受 base64，canonical 层不放 base64（providers.md §2 规则 2）。 */
public data class AttachmentBytes(public val mime: String, public val base64: String)
