package com.neochat.app.chat

import com.neochat.protocol.UsageCounts
import com.neochat.protocol.errors.ErrorObject

/** 会话阶段的三种可见状态（连接中 / 流式中 / 已停止）外加空闲与失败。 */
public enum class ChatPhase {
    IDLE,
    CONNECTING,
    STREAMING,
    STOPPED,
    FAILED,
}

/** 消息归属。 */
public enum class MessageRole {
    USER,
    ASSISTANT,
}

/** 裁决后的一条消息。`error` 一旦写入就一直留在这一轮里（横幅关掉后降级为行式错误，不丢）。 */
public data class ChatMessageUi(
    public val id: Long,
    public val role: MessageRole,
    public val text: String = "",
    public val reasoning: String = "",
    public val streaming: Boolean = false,
    public val interrupted: Boolean = false,
    public val error: ErrorObject? = null,
    public val errorBannerVisible: Boolean = true,
    public val usage: UsageCounts? = null,
)

/** 从库里读回的一条消息：重绘只需要角色、正文、思考与是否被打断。 */
public data class RestoredMessage(
    public val role: MessageRole,
    public val text: String,
    public val reasoning: String,
    public val interrupted: Boolean,
)

/** 一次对话的完整界面状态；界面只读它，任何改动都经过 [ChatStateMachine]。 */
public data class ChatUiState(
    public val conversationId: String? = null,
    public val sessionStarted: Boolean = false,
    public val messages: List<ChatMessageUi> = emptyList(),
    public val draft: String = "",
    public val phase: ChatPhase = ChatPhase.IDLE,
) {
    /** 有请求在飞（连接或流式中）。 */
    public val isBusy: Boolean
        get() = phase == ChatPhase.CONNECTING || phase == ChatPhase.STREAMING
}

/** 送入请求层的一轮上下文。 */
public data class ChatTurn(public val role: MessageRole, public val text: String)

/** 一次待发出的请求：本次文本 + 完整历史（含本次用户消息）。 */
public data class ChatSubmission(public val text: String, public val history: List<ChatTurn>)
