package com.neochat.app.chat

import com.neochat.app.data.ChatStore
import com.neochat.app.data.ConversationSummary
import com.neochat.app.data.SearchHit
import com.neochat.app.data.StoreSnapshot
import com.neochat.app.data.StoredMessage
import com.neochat.app.data.StoredStatus
import com.neochat.app.data.TurnRefs
import com.neochat.protocol.UsageCounts
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow

/** ViewModel 测试用的内存实现：记录调用顺序，不碰 Room。 */
internal class FakeChatStore : ChatStore {

    val summaryFlow: MutableStateFlow<List<ConversationSummary>> = MutableStateFlow(emptyList())
    val archivedFlow: MutableStateFlow<List<ConversationSummary>> = MutableStateFlow(emptyList())

    var latestConversationId: String? = null
    var restoreMessages: List<StoredMessage> = emptyList()
    var openResult: StoreSnapshot? = null

    val created: MutableList<String> = mutableListOf()
    val deleted: MutableList<String> = mutableListOf()
    val archived: MutableList<String> = mutableListOf()
    val unarchived: MutableList<String> = mutableListOf()
    var searchHits: List<SearchHit> = emptyList()
    val appendedText: StringBuilder = StringBuilder()
    val appendedReasoning: StringBuilder = StringBuilder()
    val finished: MutableList<StoredStatus> = mutableListOf()
    var lastErrorJson: String? = null
    var lastUsage: UsageCounts? = null
    var reloaded: MutableList<String> = mutableListOf()
    val deletedMessages: MutableList<String> = mutableListOf()

    private var sequence: Int = 0

    override suspend fun open(): StoreSnapshot {
        openResult?.let { return it }
        return StoreSnapshot(latestConversationId = latestConversationId, sweptStreaming = 0)
    }

    override fun conversations(): Flow<List<ConversationSummary>> = summaryFlow

    override fun archivedConversations(): Flow<List<ConversationSummary>> = archivedFlow

    override suspend fun archiveConversation(id: String) {
        archived += id
    }

    override suspend fun unarchiveConversation(id: String) {
        unarchived += id
    }

    override suspend fun search(query: String): List<SearchHit> = searchHits

    override suspend fun createConversation(): String {
        sequence += 1
        val id = "conversation-$sequence"
        created += id
        return id
    }

    override suspend fun deleteConversation(id: String) {
        deleted += id
    }

    override suspend fun loadMessages(conversationId: String): List<StoredMessage> {
        reloaded += conversationId
        return restoreMessages
    }

    override suspend fun beginTurn(conversationId: String, userText: String, model: String?): TurnRefs {
        sequence += 1
        return TurnRefs(
            conversationId = conversationId,
            userMessageId = "user-$sequence",
            assistantMessageId = "assistant-$sequence",
            textPartId = "text-$sequence",
            reasoningPartId = "reasoning-$sequence",
        )
    }

    override suspend fun beginRetry(conversationId: String, parentMessageId: String?, model: String?): TurnRefs {
        sequence += 1
        return TurnRefs(
            conversationId = conversationId,
            userMessageId = parentMessageId.orEmpty(),
            assistantMessageId = "assistant-$sequence",
            textPartId = "text-$sequence",
            reasoningPartId = "reasoning-$sequence",
        )
    }

    override fun appendText(textPartId: String, delta: String) {
        appendedText.append(delta)
    }

    override fun appendReasoning(reasoningPartId: String, delta: String) {
        appendedReasoning.append(delta)
    }

    override fun recordUsage(conversationId: String, messageId: String, model: String?, counts: UsageCounts) {
        lastUsage = counts
    }

    override fun finish(turn: TurnRefs, status: StoredStatus, finishReason: String?, errorJson: String?) {
        finished += status
        lastErrorJson = errorJson
    }

    override suspend fun softDeleteMessage(messageId: String): Int {
        deletedMessages += messageId
        return 1
    }

    override suspend fun quiesce(): Unit = Unit
}
