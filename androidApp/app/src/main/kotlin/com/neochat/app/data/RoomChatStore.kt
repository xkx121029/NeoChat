package com.neochat.app.data

import androidx.room.Transactor
import androidx.room.useWriterConnection
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
 *
 * 全文索引交给 [MessageIndex]：text part 的正文与它的 FTS 行永远在**同一事务**里写，
 * 软删时同一事务里清行（`v1.sql` 头部注释）。
 */
internal class RoomChatStore(
    private val db: NeoChatDatabase,
    private val now: () -> Long = System::currentTimeMillis,
    private val writer: CoroutineDispatcher = newWriter(),
) : ChatStore {

    private val scope: CoroutineScope = CoroutineScope(SupervisorJob() + writer)

    private val pending: MutableList<Job> = mutableListOf()

    private val index: MessageIndex = MessageIndex(db)

    private val device: DeviceIdentity = DeviceIdentity(db, now)

    override suspend fun open(): StoreSnapshot = withContext(writer) {
        val at = now()
        val swept = db.messages().abortStreaming(at, Uuid7.revision())
        device.get()
        StoreSnapshot(latestConversationId = db.conversations().mostRecent()?.id, sweptStreaming = swept)
    }

    override fun conversations(): Flow<List<ConversationSummary>> = summaries(db.conversations().observeAlive())

    override fun archivedConversations(): Flow<List<ConversationSummary>> =
        summaries(db.conversations().observeArchived())

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
                originDevice = device.get(),
                createdAt = at,
                updatedAt = at,
                deletedAt = null,
            ),
        )
        id
    }

    override suspend fun archiveConversation(id: String): Unit = withContext(writer) {
        db.conversations().setArchived(id, true, now(), Uuid7.revision())
    }

    override suspend fun unarchiveConversation(id: String): Unit = withContext(writer) {
        db.conversations().setArchived(id, false, now(), Uuid7.revision())
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

    override suspend fun search(query: String): List<SearchHit> = withContext(writer) {
        val expression = SearchText.matchExpression(query) ?: return@withContext emptyList()
        index.search(expression)
    }

    override suspend fun beginTurn(conversationId: String, userText: String, model: String?): TurnRefs =
        withContext(writer) {
            writeTransaction {
                val at = now()
                val conversation = db.conversations().find(conversationId)
                val parentId = conversation?.activeLeafMessageId
                val userMessageId = Uuid7.next(at)
                db.messages().insert(
                    messageRow(
                        id = userMessageId,
                        conversationId = conversationId,
                        parentId = parentId,
                        role = ROLE_USER,
                        siblingIndex = db.messages().siblingCount(conversationId, parentId),
                        status = StoredStatus.COMPLETE,
                        at = at,
                        model = model,
                        originDevice = device.get(),
                    ),
                )
                val userPartId = Uuid7.next(at)
                db.parts().insert(partRow(userPartId, userMessageId, USER_TEXT_INDEX, KIND_TEXT, userText, at))
                index.write(userPartId)
                if (conversation != null && conversation.titleAuto && conversation.title.isBlank()) {
                    db.conversations().retitleIfAuto(conversationId, titleOf(userText), at, Uuid7.revision())
                }
                val assistant = insertAssistant(conversationId, userMessageId, model, at)
                db.conversations().touch(conversationId, at, 2, assistant.assistantMessageId, Uuid7.revision())
                assistant.copy(userMessageId = userMessageId)
            }
        }

    override suspend fun beginRetry(conversationId: String, parentMessageId: String?, model: String?): TurnRefs =
        withContext(writer) {
            writeTransaction { insertAssistant(conversationId, parentMessageId, model, now()) }
        }

    override fun appendText(textPartId: String, delta: String) {
        enqueue {
            val at = now()
            writeTransaction {
                db.parts().append(textPartId, delta, at)
                index.write(textPartId)
            }
        }
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

    override suspend fun softDeleteMessage(messageId: String): Int = withContext(writer) {
        writeTransaction {
            val target = db.messages().find(messageId) ?: return@writeTransaction 0
            val at = now()
            val rev = Uuid7.revision()
            val ids = db.messages().subtreeIds(messageId)
            if (ids.isEmpty()) return@writeTransaction 0
            db.messages().softDelete(ids, at, rev)
            index.remove(ids)
            val leaf = db.conversations().find(target.conversationId)?.activeLeafMessageId
            val nextLeaf = if (leaf != null && leaf in ids) target.parentId else leaf
            db.conversations().retouchAfterDelete(target.conversationId, nextLeaf, at, rev)
            ids.size
        }
    }

    /**
     * 写事务。自带 SQLite 走驱动路径后 `androidx.room.withTransaction`（SupportSQLite 入口）
     * 不可用：它会去要 `openHelper`。这里拿写连接开 IMMEDIATE 事务，内部的 DAO 调用会被
     * Room 认作同一协程的连接，从而加入同一个事务。
     */
    private suspend fun <T> writeTransaction(block: suspend () -> T): T =
        db.useWriterConnection { connection ->
            connection.withTransaction(Transactor.SQLiteTransactionType.IMMEDIATE) { block() }
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
            messageRow(
                id = assistantId,
                conversationId = conversationId,
                parentId = parentId,
                role = ROLE_ASSISTANT,
                siblingIndex = db.messages().siblingCount(conversationId, parentId),
                status = StoredStatus.STREAMING,
                at = at,
                model = model,
                originDevice = device.get(),
            ),
        )
        val reasoningPartId = Uuid7.next(at)
        db.parts().insert(partRow(reasoningPartId, assistantId, ASSISTANT_REASONING_INDEX, KIND_REASONING, "", at))
        val textPartId = Uuid7.next(at)
        db.parts().insert(partRow(textPartId, assistantId, ASSISTANT_TEXT_INDEX, KIND_TEXT, "", at))
        index.write(textPartId)
        db.conversations().touch(conversationId, at, 1, assistantId, Uuid7.revision())
        return TurnRefs(
            conversationId = conversationId,
            userMessageId = parentId.orEmpty(),
            assistantMessageId = assistantId,
            textPartId = textPartId,
            reasoningPartId = reasoningPartId,
        )
    }

    private companion object {
        const val ROLE_USER = "user"
        const val ROLE_ASSISTANT = "assistant"
        const val KIND_TEXT = "text"
        const val KIND_REASONING = "reasoning"
        const val USER_TEXT_INDEX = 0L
        const val ASSISTANT_REASONING_INDEX = 0L
        const val ASSISTANT_TEXT_INDEX = 1L
        const val PROVIDER_ID = "openai_compatible"
        const val DEFAULT_CURRENCY = "USD"

        fun newWriter(): CoroutineDispatcher =
            Executors.newSingleThreadExecutor { runnable -> Thread(runnable, "neochat-db-writer") }
                .asCoroutineDispatcher()
    }
}

/** 库里的会话行 → 列表用的一行。 */
private fun summaries(source: Flow<List<ConversationEntity>>): Flow<List<ConversationSummary>> =
    source.map { rows ->
        rows.map { row ->
            ConversationSummary(
                id = row.id,
                title = row.title,
                lastActivityAt = row.lastMessageAt ?: row.createdAt,
                messageCount = row.messageCount,
            )
        }
    }

/** 一条消息行；`rev` 与 `sync_version` 为将来同步留位。 */
private fun messageRow(
    id: String,
    conversationId: String,
    parentId: String?,
    role: String,
    siblingIndex: Long,
    status: StoredStatus,
    at: Long,
    model: String?,
    originDevice: String,
): MessageEntity = MessageEntity(
    id = id,
    conversationId = conversationId,
    parentId = parentId,
    role = role,
    siblingIndex = siblingIndex,
    status = status.wire,
    finishReason = null,
    errorJson = null,
    providerId = "openai_compatible",
    modelId = model,
    truncated = false,
    syncVersion = 1,
    rev = Uuid7.revision(),
    originDevice = originDevice,
    createdAt = at,
    updatedAt = at,
    deletedAt = null,
)

private fun partRow(id: String, messageId: String, partIndex: Long, kind: String, body: String, at: Long) =
    MessagePartEntity(
        id = id,
        messageId = messageId,
        partIndex = partIndex,
        kind = kind,
        body = body,
        metaJson = null,
        truncated = false,
        createdAt = at,
        updatedAt = at,
    )

/** 标题：折叠空白，按码点截 24 字，超出加省略号（与 Windows 端同一规则）。 */
private fun titleOf(text: String): String {
    val folded = text.replace(TITLE_WHITESPACE, " ").trim()
    val points = folded.codePoints().toArray()
    return if (points.size <= TITLE_LIMIT) folded else String(points, 0, TITLE_LIMIT) + "…"
}

private const val TITLE_LIMIT = 24
private val TITLE_WHITESPACE = Regex("\\s+")
