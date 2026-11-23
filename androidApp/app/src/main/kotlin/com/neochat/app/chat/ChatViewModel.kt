package com.neochat.app.chat

import com.neochat.app.data.ChatStore
import com.neochat.app.data.ConversationSummary
import com.neochat.app.data.StoredRole
import com.neochat.app.data.StoredStatus
import com.neochat.app.data.TurnRefs
import com.neochat.protocol.FinishReason
import com.neochat.protocol.Json
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
import kotlinx.coroutines.flow.collect
import kotlinx.coroutines.launch
import com.neochat.protocol.request.ChatMessage as RequestMessage

/**
 * 单向数据流的一端：界面只读 [state] 与 [conversations]、只发意图；状态推进全部落在
 * [ChatStateMachine]，请求生命周期留在这里，落盘交给 [ChatStore]。
 *
 * 落盘与请求是两条线：`beginTurn` 先写库（拿到 db id），随后每个流事件先追加 part 再更新界面；
 * 界面上的消息 id 是进程内短 id，`databaseIds` 负责映射到库里的 UUIDv7。
 */
public class ChatViewModel(
    private val engine: ChatEngine,
    private val provider: ProviderConfigStore,
    private val store: ChatStore,
    private val scope: CoroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate),
) {

    private val machine: ChatStateMachine = ChatStateMachine()
    private val mutableState: MutableStateFlow<ChatUiState> = MutableStateFlow(machine.state)
    private val mutableConversations: MutableStateFlow<List<ConversationSummary>> =
        MutableStateFlow(emptyList())
    private val mutableArchived: MutableStateFlow<List<ConversationSummary>> = MutableStateFlow(emptyList())

    public val state: StateFlow<ChatUiState> = mutableState.asStateFlow()

    public val conversations: StateFlow<List<ConversationSummary>> = mutableConversations.asStateFlow()

    /** "已归档"入口：`conversation.archived = 1` 的那些会话。 */
    public val archivedConversations: StateFlow<List<ConversationSummary>> = mutableArchived.asStateFlow()

    private var activeRun: ChatRun? = null
    private var activeTurn: TurnRefs? = null
    private var generation: Long = 0
    private var conversationId: String? = null
    private val databaseIds: MutableMap<Long, String> = mutableMapOf()

    init {
        scope.launch { store.conversations().collect { mutableConversations.value = it } }
        scope.launch { store.archivedConversations().collect { mutableArchived.value = it } }
        scope.launch { restoreLast() }
    }

    public fun startSession() {
        publish(machine.startSession())
    }

    public fun updateDraft(text: String) {
        publish(machine.updateDraft(text))
    }

    public fun submit() {
        val submission = machine.submit() ?: return
        publish(machine.state)
        val messages = machine.state.messages
        val assistantLocalId = messages.last().id
        val userLocalId = messages[messages.size - 2].id
        detach()
        val token = generation
        scope.launch {
            val turn = beginTurn(submission) ?: return@launch
            databaseIds[userLocalId] = turn.userMessageId
            databaseIds[assistantLocalId] = turn.assistantMessageId
            if (token != generation) return@launch
            activeTurn = turn
            startStream(submission, token)
        }
    }

    public fun retry() {
        val submission = machine.retry() ?: return
        publish(machine.state)
        val messages = machine.state.messages
        val assistantLocalId = messages.last().id
        val parentLocalId = messages.dropLast(1).lastOrNull { it.role == MessageRole.USER }?.id
        detach()
        val token = generation
        scope.launch {
            val turn = beginRetry(parentLocalId) ?: return@launch
            databaseIds[assistantLocalId] = turn.assistantMessageId
            if (token != generation) return@launch
            activeTurn = turn
            startStream(submission, token)
        }
    }

    public fun stop() {
        if (!machine.state.isBusy) return
        detach()
        publish(machine.requestCancel())
    }

    /**
     * 长按菜单"重新生成"：同父新兄弟分支。库里挂在新助手消息上的 `parent_id` 就是被重开那条
     * 回复回答的用户消息，因此旧回复不会被覆盖。
     */
    public fun regenerate(messageId: Long) {
        val submission = machine.regenerate(messageId) ?: return
        publish(machine.state)
        val messages = machine.state.messages
        val assistantLocalId = messages.last().id
        val parentLocalId = messages[messages.size - 2].id
        detach()
        val token = generation
        scope.launch {
            val turn = beginRetry(parentLocalId) ?: return@launch
            databaseIds[assistantLocalId] = turn.assistantMessageId
            if (token != generation) return@launch
            activeTurn = turn
            startStream(submission, token)
        }
    }

    /** 长按菜单"删除"：先撤界面，再写 `deleted_at`（含分支、FTS 与活跃叶，见落盘层）。 */
    public fun deleteMessage(messageId: Long) {
        val target = machine.state.messages.firstOrNull { it.id == messageId } ?: return
        if (target.streaming) return
        val databaseId = databaseIds[messageId]
        val removed = machine.state.messages
            .dropWhile { it.id != messageId }
            .map { it.id }
        publish(machine.deleteMessage(messageId))
        removed.forEach(databaseIds::remove)
        if (databaseId == null) return
        scope.launch { runCatching { store.softDeleteMessage(databaseId) } }
    }

    /** 顶栏"新对话"：上一条空会话直接复用，避免列表里堆一串没说话的会话。 */
    public fun startNewConversation() {
        detach()
        publish(machine.reset())
        scope.launch {
            val current = conversationId
            val reusable = mutableConversations.value.any { it.id == current && it.messageCount == 0L }
            if (!reusable) {
                conversationId = runCatching { store.createConversation() }.getOrNull()
            }
            databaseIds.clear()
            publish(machine.startSession())
        }
    }

    /** 从列表点开一个历史会话：取消在飞的请求，读回活跃分支。 */
    public fun openConversation(id: String) {
        detach()
        publish(machine.reset())
        scope.launch { showConversation(id) }
    }

    /**
     * 归档当前会话等于把它从工作区收起来：和删除一样先撤界面、再开一段空白会话，
     * 区别只是库里 `archived=1`，消息与全文索引都留着。
     */
    public fun archiveConversation(id: String) {
        scope.launch {
            runCatching { store.archiveConversation(id) }
            if (id != conversationId) return@launch
            detach()
            conversationId = null
            databaseIds.clear()
            publish(machine.reset())
        }
    }

    public fun unarchiveConversation(id: String) {
        scope.launch { runCatching { store.unarchiveConversation(id) } }
    }

    public fun deleteConversation(id: String) {
        scope.launch {
            runCatching { store.deleteConversation(id) }
            if (id != conversationId) return@launch
            detach()
            conversationId = null
            databaseIds.clear()
            publish(machine.reset())
        }
    }

    public fun dismissError(messageId: Long) {
        publish(machine.dismissError(messageId))
    }

    public fun close() {
        detach()
        engine.close()
        scope.cancel()
    }

    private suspend fun restoreLast() {
        val snapshot = runCatching { store.open() }.getOrNull() ?: return
        val id = snapshot.latestConversationId ?: return
        showConversation(id)
    }

    private suspend fun showConversation(id: String) {
        val stored = runCatching { store.loadMessages(id) }.getOrDefault(emptyList())
        conversationId = id
        databaseIds.clear()
        publish(
            machine.restore(
                stored.map { message ->
                    RestoredMessage(
                        role = if (message.role == StoredRole.USER) MessageRole.USER else MessageRole.ASSISTANT,
                        text = message.text,
                        reasoning = message.reasoning,
                        interrupted = message.status != StoredStatus.COMPLETE,
                    )
                },
            ),
        )
        stored.forEachIndexed { index, message ->
            machine.state.messages.getOrNull(index)?.let { databaseIds[it.id] = message.id }
        }
    }

    private suspend fun beginTurn(submission: ChatSubmission): TurnRefs? = try {
        store.beginTurn(ensureConversation(), submission.text, provider.config.model)
    } catch (failure: Throwable) {
        publish(machine.failWithoutStream(ErrorMapper.internal(facts(), failure.toString())))
        null
    }

    private suspend fun beginRetry(parentLocalId: Long?): TurnRefs? = try {
        store.beginRetry(ensureConversation(), parentLocalId?.let { databaseIds[it] }, provider.config.model)
    } catch (failure: Throwable) {
        publish(machine.failWithoutStream(ErrorMapper.internal(facts(), failure.toString())))
        null
    }

    private suspend fun ensureConversation(): String =
        conversationId ?: store.createConversation().also { conversationId = it }

    private fun startStream(submission: ChatSubmission, token: Long) {
        val started = runCatching { engine.start(buildRequest(submission)) { event -> onEvent(token, event) } }
        activeRun = started.getOrNull()
        started.exceptionOrNull()?.let { failure ->
            val error = ErrorMapper.internal(facts(), failure.toString())
            settleTurn(StoredStatus.FAILED, null, Json.write(error.toJson()))
            publish(machine.failWithoutStream(error))
        }
    }

    /** 起新请求前先把上一路回调作废：`generation` 一变，旧句柄再送什么进来都被丢掉。 */
    private fun detach() {
        generation += 1
        activeRun?.cancel()
        activeRun = null
        settleTurn(StoredStatus.ABORTED, null, null)
    }

    private fun settleTurn(status: StoredStatus, finishReason: String?, errorJson: String?) {
        val turn = activeTurn ?: return
        activeTurn = null
        store.finish(turn, status, finishReason, errorJson)
    }

    private fun onEvent(token: Long, event: StreamEvent) {
        if (token != generation) return
        persist(event)
        scope.launch {
            if (token != generation) return@launch
            publish(machine.onStreamEvent(event))
            if (event is StreamEvent.Done || event is StreamEvent.Failed) activeRun = null
        }
    }

    private fun persist(event: StreamEvent) {
        val turn = activeTurn ?: return
        when (event) {
            is StreamEvent.Delta -> store.appendText(turn.textPartId, event.text)
            is StreamEvent.ReasoningDelta -> store.appendReasoning(turn.reasoningPartId, event.text)
            is StreamEvent.Usage -> store.recordUsage(
                conversationId = turn.conversationId,
                messageId = turn.assistantMessageId,
                model = provider.config.model,
                counts = event.counts,
            )
            is StreamEvent.Done -> {
                val aborted = event.finishReason == FinishReason.ABORTED_USER
                settleTurn(if (aborted) StoredStatus.ABORTED else StoredStatus.COMPLETE, event.finishReason.wire, null)
            }
            is StreamEvent.Failed -> settleTurn(StoredStatus.FAILED, null, Json.write(event.error.toJson()))
            is StreamEvent.ToolCallDelta, is StreamEvent.ToolCallEnd, is StreamEvent.Notice -> Unit
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
        mutableState.value = next.copy(conversationId = conversationId)
    }
}
