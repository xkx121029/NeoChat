package com.neochat.app.chat

import com.neochat.protocol.StreamEvent
import com.neochat.protocol.errors.ErrorMapper
import com.neochat.protocol.errors.ProviderFacts
import com.neochat.protocol.request.ChatRequest
import com.neochat.protocol.request.ChatRole
import com.neochat.protocol.request.MessagePart
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import com.neochat.protocol.request.ChatMessage as RequestMessage

/**
 * 单向数据流的一端：界面只读 [state]、只发意图；状态推进全部落在 [ChatStateMachine]，
 * 请求生命周期（开流、取消、丢弃过期回调）留在这一层。
 */
public class ChatViewModel(
    private val engine: ChatEngine,
    private val provider: ProviderConfigStore,
    private val scope: CoroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate),
) {

    private val machine: ChatStateMachine = ChatStateMachine()
    private val mutableState: MutableStateFlow<ChatUiState> = MutableStateFlow(machine.state)

    public val state: StateFlow<ChatUiState> = mutableState.asStateFlow()

    private var activeRun: ChatRun? = null
    private var generation: Long = 0

    public fun startSession() {
        publish(machine.startSession())
    }

    public fun reset() {
        detach()
        publish(machine.reset())
    }

    public fun updateDraft(text: String) {
        publish(machine.updateDraft(text))
    }

    public fun submit() {
        dispatch(machine.submit())
    }

    public fun retry() {
        dispatch(machine.retry())
    }

    public fun stop() {
        if (!machine.state.isBusy) return
        detach()
        publish(machine.requestCancel())
    }

    public fun dismissError(messageId: Long) {
        publish(machine.dismissError(messageId))
    }

    public fun close() {
        detach()
        engine.close()
        scope.cancel()
    }

    private fun dispatch(submission: ChatSubmission?) {
        if (submission == null) return
        publish(machine.state)
        detach()
        val token = generation
        val request = buildRequest(submission)
        val started = runCatching { engine.start(request) { event -> onEvent(token, event) } }
        activeRun = started.getOrNull()
        started.exceptionOrNull()?.let { failure ->
            publish(machine.failWithoutStream(ErrorMapper.internal(facts(), failure.toString())))
        }
    }

    /** 起新请求前先把上一路回调作废：`generation` 一变，旧句柄再送什么进来都被丢掉。 */
    private fun detach() {
        generation += 1
        activeRun?.cancel()
        activeRun = null
    }

    private fun onEvent(token: Long, event: StreamEvent) {
        if (token != generation) return
        scope.launch {
            if (token != generation) return@launch
            publish(machine.onStreamEvent(event))
            if (event is StreamEvent.Done || event is StreamEvent.Failed) activeRun = null
        }
    }

    private fun buildRequest(submission: ChatSubmission): ChatRequest = ChatRequest(
        model = provider.config.model,
        messages = submission.history.map { turn ->
            RequestMessage(
                role = if (turn.role == MessageRole.USER) ChatRole.USER else ChatRole.ASSISTANT,
                parts = listOf(MessagePart.Text(turn.text)),
            )
        },
    )

    private fun facts(): ProviderFacts = ProviderFacts(
        provider = ProviderConfigStore.PROVIDER_ID,
        model = provider.config.model,
    )

    private fun publish(next: ChatUiState) {
        mutableState.value = next
    }
}
