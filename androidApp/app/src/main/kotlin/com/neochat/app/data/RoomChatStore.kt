package com.neochat.app.data

import com.neochat.protocol.UsageCounts
import java.util.concurrent.Executors
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.asCoroutineDispatcher
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * [ChatStore] 的 Room 实现。所有写操作排在同一个单线程 dispatcher 上：队列顺序 = 调用顺序，
 * 所以 `beginTurn` → 若干 `append` → `finish` 不会乱序；流式期间进程被杀只丢掉未提交的那几条。
 */
internal class RoomChatStore(
    private val db: NeoChatDatabase,
    private val now: () -> Long = System::currentTimeMillis,
    private val writer: CoroutineDispatcher = newWriter(),
) : ChatStore {

    private val scope: CoroutineScope = CoroutineScope(SupervisorJob() + writer)

    private val pending: MutableList<Job> = mutableListOf()

    private var cachedDeviceId: String? = null

    override suspend fun open(): StoreSnapshot = withContext(writer) {
        val at = now()
        val swept = db.messages().abortStreaming(at, Uuid7.revision())
        deviceId()
        StoreSnapshot(latestConversationId = db.conversations().mostRecent()?.id, sweptStreaming = swept)
    }

    override fun conversations(): Flow<List<ConversationSummary>> =
        db.conversations().observeAlive().map { rows ->
            rows.map { row ->
                ConversationSummary(
                    id = row.id,
                    title = row.title,
                    lastActivityAt = row.lastMessageAt ?: row.createdAt,
                    messageCount = row.messageCount,
                )
            }
        }

    override suspend fun createConversation(): String = withContext(writer) {
        val at = now()
        val id = Uuid7.next(at)
        db.conversations().insert(
            ConversationEntity(
                id = id,
                title = "",
                titleAuto = true,
                agentId = null,
                folderId = null,
                pinned = false,
                archived = false,
                activeLeafMessageId = null,
                settingsJson = null,
                lastMessageAt = null,
                messageCount = 0,
                syncVersion = 1,
                rev = Uuid7.revision(),
                originDevice = deviceId(),
                createdAt = at,
                updatedAt = at,
                deletedAt = null,
            ),
        )
        id
    }

    override suspend fun deleteConversation(id: String): Unit = withContext(writer) {
        db.conversations().softDelete(id, now(), Uuid7.revision())
    }

    override suspend fun loadMessages(conversationId: String): List<StoredMessage> = withContext(writer) {
        val rows = db.messages().listForConversation(conversationId)
        val parts = db.parts().listForConversation(conversationId).groupBy { it.messageId }
        val byId = rows.associateBy { it.id }
        val leaf = db.conversations().find(conversationId)?.activeLeafMessageId
        val branch = mutableListOf<MessageEntity>()
        var cursor = leaf?.let { byId[it] }
        while (cursor != null) {
            branch.add(cursor)
            cursor = cursor.parentId?.let { byId[it] }
        }
        if (branch.isEmpty()) branch.addAll(rows)
        branch.reverse()
        branch.map { row ->
            val own = parts[row.id].orEmpty()
            StoredMessage(
                id = row.id,
                role = if (row.role == ROLE_USER) StoredRole.USER else StoredRole.ASSISTANT,
                text = own.firstOrNull { it.kind == KIND_TEXT }?.body.orEmpty(),
                reasoning = own.firstOrNull { it.kind == KIND_REASONING }?.body.orEmpty(),
                status = StoredStatus.of(row.status),
            )
        }
    }

    override suspend fun beginTurn(conversationId: String, userText: String, model: String?): TurnRefs =
        withContext(writer) {
            val at = now()
            val conversation = db.conversations().find(conversationId)
            val parentId = conversation?.activeLeafMessageId
            val userMessageId = Uuid7.next(at)
            db.messages().insert(
                messageEntity(
                    id = userMessageId,
                    conversationId = conversationId,
                    parentId = parentId,
                    role = ROLE_USER,
                    siblingIndex = db.messages().siblingCount(conversationId, parentId),
                    status = StoredStatus.COMPLETE,
                    at = at,
                    model = model,
                ),
            )
            db.parts().insert(partEntity(Uuid7.next(at), userMessageId, USER_TEXT_INDEX, KIND_TEXT, userText, at))
            if (conversation != null && conversation.titleAuto && conversation.title.isBlank()) {
                db.conversations().retitleIfAuto(conversationId, titleOf(userText), at, Uuid7.revision())
            }
            val assistant = insertAssistant(conversationId, userMessageId, model, at)
            db.conversations().touch(conversationId, at, 2, assistant.assistantMessageId, Uuid7.revision())
            assistant.copy(userMessageId = userMessageId)
        }

    override suspend fun beginRetry(conversationId: String, parentMessageId: String?, model: String?): TurnRefs =
        withContext(writer) { insertAssistant(conversationId, parentMessageId, model, now()) }

    override fun appendText(textPartId: String, delta: String) {
        val at = now()
        enqueue { db.parts().append(textPartId, delta, at) }
    }

    override fun appendReasoning(reasoningPartId: String, delta: String) {
        val at = now()
        enqueue { db.parts().append(reasoningPartId, delta, at) }
    }

    override fun recordUsage(conversationId: String, messageId: String, model: String?, counts: UsageCounts) {
        val at = now()
        enqueue {
            db.usage().insert(
                UsageRecordEntity(
                    id = Uuid7.next(at),
                    messageId = messageId,
                    providerId = PROVIDER_ID,
                    modelId = model,
                    conversationId = conversationId,
                    inputTokens = counts.input,
                    outputTokens = counts.output,
                    cachedTokens = counts.cached ?: 0,
                    reasoningTokens = counts.reasoning ?: 0,
                    costEstimate = counts.providerCost?.amount?.toDoubleOrNull(),
                    providerCost = null,
                    currency = counts.providerCost?.currency ?: DEFAULT_CURRENCY,
                    priceSnapshotJson = null,
                    isEstimate = counts.estimated ?: false,
                    errorBoundPct = counts.errorBoundPct,
                    createdAt = at,
                ),
            )
        }
    }

    override fun finish(turn: TurnRefs, status: StoredStatus, finishReason: String?, errorJson: String?) {
        val at = now()
        enqueue {
            db.messages().settle(turn.assistantMessageId, status.wire, finishReason, errorJson, at, Uuid7.revision())
            db.conversations().touch(turn.conversationId, at, 0, null, Uuid7.revision())
        }
    }

    /** 写任务入队；`quiesce` 靠这些句柄等到真正提交完，而不是只等一个空任务。 */
    private fun enqueue(block: suspend () -> Unit) {
        val job = scope.launch { block() }
        synchronized(pending) { pending.add(job) }
    }

    override suspend fun quiesce() {
        while (true) {
            val jobs = synchronized(pending) {
                pending.toList().also { pending.clear() }
            }
            if (jobs.isEmpty()) return
            jobs.forEach { it.join() }
        }
    }

    fun close() {
        scope.cancel()
    }

    private suspend fun insertAssistant(
        conversationId: String,
        parentId: String?,
        model: String?,
        at: Long,
    ): TurnRefs {
        val assistantId = Uuid7.next(at)
        db.messages().insert(
            messageEntity(
                id = assistantId,
                conversationId = conversationId,
                parentId = parentId,
                role = ROLE_ASSISTANT,
                siblingIndex = db.messages().siblingCount(conversationId, parentId),
                status = StoredStatus.STREAMING,
                at = at,
                model = model,
            ),
        )
        val reasoningPartId = Uuid7.next(at)
        db.parts().insert(partEntity(reasoningPartId, assistantId, ASSISTANT_REASONING_INDEX, KIND_REASONING, "", at))
        val textPartId = Uuid7.next(at)
        db.parts().insert(partEntity(textPartId, assistantId, ASSISTANT_TEXT_INDEX, KIND_TEXT, "", at))
        db.conversations().touch(conversationId, at, 1, assistantId, Uuid7.revision())
        return TurnRefs(
            conversationId = conversationId,
            userMessageId = parentId.orEmpty(),
            assistantMessageId = assistantId,
            textPartId = textPartId,
            reasoningPartId = reasoningPartId,
        )
    }

    private suspend fun messageEntity(
        id: String,
        conversationId: String,
        parentId: String?,
        role: String,
        siblingIndex: Long,
        status: StoredStatus,
        at: Long,
        model: String?,
    ): MessageEntity = MessageEntity(
        id = id,
        conversationId = conversationId,
        parentId = parentId,
        role = role,
        siblingIndex = siblingIndex,
        status = status.wire,
        finishReason = null,
        errorJson = null,
        providerId = PROVIDER_ID,
        modelId = model,
        truncated = false,
        syncVersion = 1,
        rev = Uuid7.revision(),
        originDevice = deviceId(),
        createdAt = at,
        updatedAt = at,
        deletedAt = null,
    )

    private fun partEntity(id: String, messageId: String, index: Long, kind: String, body: String, at: Long) =
        MessagePartEntity(
            id = id,
            messageId = messageId,
            partIndex = index,
            kind = kind,
            body = body,
            metaJson = null,
            truncated = false,
            createdAt = at,
            updatedAt = at,
        )

    /** 标题：折叠空白，按码点截 24 字，超出加省略号（与 Windows 端同一规则）。 */
    private fun titleOf(text: String): String {
        val folded = text.replace(WHITESPACE, " ").trim()
        val points = folded.codePoints().toArray()
        if (points.size <= TITLE_LIMIT) return folded
        return String(points, 0, TITLE_LIMIT) + "…"
    }

    private suspend fun deviceId(): String {
        cachedDeviceId?.let { return it }
        val stored = db.settings().value(KEY_DEVICE_ID)
        val id = stored?.trim('"') ?: Uuid7.next(now())
        if (stored == null) {
            db.settings().put(AppSettingEntity(key = KEY_DEVICE_ID, valueJson = "\"$id\"", updatedAt = now()))
        }
        cachedDeviceId = id
        return id
    }

    private companion object {
        const val ROLE_USER = "user"
        const val ROLE_ASSISTANT = "assistant"
        const val KIND_TEXT = "text"
        const val KIND_REASONING = "reasoning"
        const val USER_TEXT_INDEX = 0L
        const val ASSISTANT_REASONING_INDEX = 0L
        const val ASSISTANT_TEXT_INDEX = 1L
        const val TITLE_LIMIT = 24
        const val KEY_DEVICE_ID = "sync.device_id"
        const val PROVIDER_ID = "openai_compatible"
        const val DEFAULT_CURRENCY = "USD"
        val WHITESPACE = Regex("\\s+")

        fun newWriter(): CoroutineDispatcher =
            Executors.newSingleThreadExecutor { runnable -> Thread(runnable, "neochat-db-writer") }
                .asCoroutineDispatcher()
    }
}
