package com.neochat.app.chat

import com.neochat.protocol.FinishReason
import com.neochat.protocol.StreamEvent
import com.neochat.protocol.errors.ErrorObject

/**
 * 纯状态机：输入是用户意图与 `StreamEvent`，输出是不可变的 [ChatUiState]。
 * 不碰线程、不碰 Android、不碰网络，因此可以用一串事件直接断言状态序列（见 `ChatStateMachineTest`）。
 */
public class ChatStateMachine {

    public var state: ChatUiState = ChatUiState()
        private set

    private var nextId: Long = 0

    /** 空态点"开始新对话"后进入会话（此时还没有消息）。 */
    public fun startSession(): ChatUiState {
        state = state.copy(sessionStarted = true)
        return state
    }

    /** 顶栏"新对话"：回到空态，消息与草稿一起清掉。 */
    public fun reset(): ChatUiState {
        state = ChatUiState()
        return state
    }

    /** 冷启动或切换会话：把库里读回的消息装进状态，消息 id 从这里重新分配。 */
    public fun restore(messages: List<RestoredMessage>): ChatUiState {
        state = ChatUiState(
            sessionStarted = messages.isNotEmpty(),
            messages = messages.map { restored ->
                ChatMessageUi(
                    id = nextId++,
                    role = restored.role,
                    text = restored.text,
                    reasoning = restored.reasoning,
                    interrupted = restored.interrupted,
                )
            },
        )
        return state
    }

    public fun updateDraft(draft: String): ChatUiState {
        state = state.copy(draft = draft)
        return state
    }

    /** 提交输入框内容；空白或有请求在飞时返回 null，界面据此不发请求。 */
    public fun submit(): ChatSubmission? {
        val text = state.draft.trim()
        if (text.isEmpty() || state.isBusy) return null
        val history = state.messages.mapNotNull { it.asTurn() } + ChatTurn(MessageRole.USER, text)
        state = state.copy(
            sessionStarted = true,
            draft = "",
            phase = ChatPhase.CONNECTING,
            messages = state.messages + userMessage(text) + assistantMessage(),
        )
        return ChatSubmission(text = text, history = history)
    }

    /** 一个流事件推进一次状态。工具调用与 notice 在 M1 不渲染，保持原状态。 */
    public fun onStreamEvent(event: StreamEvent): ChatUiState {
        state = when (event) {
            is StreamEvent.Delta -> mutateAssistant { it.copy(text = it.text + event.text, streaming = true) }
                .copy(phase = ChatPhase.STREAMING)
            is StreamEvent.ReasoningDelta -> mutateAssistant { it.copy(reasoning = it.reasoning + event.text) }
                .copy(phase = ChatPhase.STREAMING)
            is StreamEvent.Usage -> mutateAssistant { it.copy(usage = event.counts) }
            is StreamEvent.Done -> finish(event.finishReason)
            is StreamEvent.Failed -> fail(event.error)
            is StreamEvent.ToolCallDelta, is StreamEvent.ToolCallEnd, is StreamEvent.Notice -> state
        }
        return state
    }

    /** 用户点了停止：立即落到"已停止"，协议随后补的 `done{aborted_user}` 再进来也不改结果。 */
    public fun requestCancel(): ChatUiState {
        if (!state.isBusy) return state
        state = mutateAssistant { it.copy(streaming = false, interrupted = true) }.copy(phase = ChatPhase.STOPPED)
        return state
    }

    /** 重试用同一份历史快照再发一次：丢掉末尾失败的助手消息，从最后一个用户消息重开。 */
    public fun retry(): ChatSubmission? {
        if (state.isBusy) return null
        val lastUser = state.messages.lastOrNull { it.role == MessageRole.USER } ?: return null
        val kept = state.messages.dropLastWhile { it.role == MessageRole.ASSISTANT }
        state = state.copy(phase = ChatPhase.CONNECTING, messages = kept + assistantMessage())
        return ChatSubmission(text = lastUser.text, history = kept.mapNotNull { it.asTurn() })
    }

    /**
     * 长按菜单里的"重新生成"：从这条助手消息重开。
     *
     * 保留它的前缀（结尾是它回答的那条用户消息），丢掉它自己和它的后继，再挂一个新的助手
     * 兄弟——落盘时 `beginRetry(parentId)` 给同一个 `parent_id`，所以是同一层的兄弟分支，
     * 旧回复留在库里可回退。
     */
    public fun regenerate(messageId: Long): ChatSubmission? {
        if (state.isBusy) return null
        val index = state.messages.indexOfFirst { it.id == messageId && it.role == MessageRole.ASSISTANT }
        if (index <= 0) return null
        val kept = state.messages.take(index)
        val lastUser = kept.lastOrNull { it.role == MessageRole.USER } ?: return null
        state = state.copy(phase = ChatPhase.CONNECTING, messages = kept + assistantMessage())
        return ChatSubmission(text = lastUser.text, history = kept.mapNotNull { it.asTurn() })
    }

    /**
     * 长按菜单里的"删除"：`deleted_at` 由落盘层写，这里只把它和它的分支从界面撤掉。
     * 消息树是线性的，一条消息的后继就是它后面的全部消息，所以子树等于这段后缀。
     */
    public fun deleteMessage(messageId: Long): ChatUiState {
        val index = state.messages.indexOfFirst { it.id == messageId }
        if (index < 0) return state
        if (state.messages[index].streaming) return state
        val kept = state.messages.take(index)
        state = state.copy(
            messages = kept,
            phase = if (state.isBusy) state.phase else ChatPhase.IDLE,
        )
        return state
    }

    /** 请求层都没能开起来（配置/客户端异常）时，把错误落在这一轮上。 */
    public fun failWithoutStream(error: ErrorObject): ChatUiState {
        if (state.messages.lastOrNull()?.role != MessageRole.ASSISTANT) {
            state = state.copy(messages = state.messages + assistantMessage())
        }
        return fail(error)
    }

    /** 关掉横幅：错误本身留在消息里，界面降级为行式错误。 */
    public fun dismissError(messageId: Long): ChatUiState {
        state = state.copy(
            messages = state.messages.map { if (it.id == messageId) it.copy(errorBannerVisible = false) else it },
        )
        return state
    }

    private fun finish(reason: FinishReason): ChatUiState {
        val interrupted = reason == FinishReason.ABORTED_USER
        val settled = mutateAssistant { it.copy(streaming = false, interrupted = it.interrupted || interrupted) }
        return settled.copy(phase = if (interrupted) ChatPhase.STOPPED else ChatPhase.IDLE)
    }

    private fun fail(error: ErrorObject): ChatUiState =
        mutateAssistant { it.copy(streaming = false, error = error) }.copy(phase = ChatPhase.FAILED)

    private fun mutateAssistant(transform: (ChatMessageUi) -> ChatMessageUi): ChatUiState {
        val lastAssistantId = state.messages.lastOrNull { it.role == MessageRole.ASSISTANT }?.id ?: return state
        return state.copy(
            messages = state.messages.map { if (it.id == lastAssistantId) transform(it) else it },
        )
    }

    private fun userMessage(text: String): ChatMessageUi = ChatMessageUi(nextId++, MessageRole.USER, text = text)

    private fun assistantMessage(): ChatMessageUi = ChatMessageUi(nextId++, MessageRole.ASSISTANT, streaming = true)

    private fun ChatMessageUi.asTurn(): ChatTurn? = when {
        text.isBlank() -> null
        else -> ChatTurn(role, text)
    }
}
