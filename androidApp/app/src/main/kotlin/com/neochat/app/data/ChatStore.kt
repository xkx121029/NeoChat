package com.neochat.app.data

import com.neochat.protocol.UsageCounts
import kotlinx.coroutines.flow.Flow

/** DDL `message.role` 的两种本期取值（`system` / `tool` 留给后续里程碑）。 */
public enum class StoredRole { USER, ASSISTANT }

/** DDL `message.status` 的闭集。 */
public enum class StoredStatus(public val wire: String) {
    STREAMING("streaming"),
    COMPLETE("complete"),
    FAILED("failed"),
    ABORTED("aborted"),
    ;

    public companion object {
        public fun of(wire: String?): StoredStatus = entries.firstOrNull { it.wire == wire } ?: COMPLETE
    }
}

/** 从库里读回的一条消息：正文与思考分别来自两个 part。 */
public data class StoredMessage(
    val id: String,
    val role: StoredRole,
    val text: String,
    val reasoning: String,
    val status: StoredStatus,
)

/** 会话列表的一行。标题为空表示用户还没说过话，界面显示占位文案。 */
public data class ConversationSummary(
    val id: String,
    val title: String,
    val lastActivityAt: Long,
    val messageCount: Long,
)

/** 冷启动结果：上一个会话（没有则为 null）+ 本次扫掉的残留 `streaming` 行数。 */
public data class StoreSnapshot(val latestConversationId: String?, val sweptStreaming: Int)

/**
 * 一轮落盘用到的四个 id：用户消息、助手消息、正文 part、思考 part。
 * part 在开轮时就建好，之后每个 delta 只做一次 `UPDATE`，被杀也只会丢最后一个 delta 之外的内容（不丢已提交部分）。
 */
public data class TurnRefs(
    val conversationId: String,
    val userMessageId: String,
    val assistantMessageId: String,
    val textPartId: String,
    val reasoningPartId: String,
)

/**
 * 持久化端口。界面层只依赖它，不依赖 Room；写路径全部排进单线程队列，保证
 * "先建消息、再追加正文、最后落状态" 的顺序与调用顺序一致。
 */
public interface ChatStore {

    /** 冷启动：把上次残留的 `streaming` 扫成 `aborted`，返回最近的会话 id。 */
    suspend fun open(): StoreSnapshot

    fun conversations(): Flow<List<ConversationSummary>>

    suspend fun createConversation(): String

    suspend fun deleteConversation(id: String)

    /** 从 `active_leaf_message_id` 回溯出活跃分支，按时间正序返回。 */
    suspend fun loadMessages(conversationId: String): List<StoredMessage>

    /** 插入用户消息与助手消息（助手 `status='streaming'`），并顺手更新会话的标题与活跃叶。 */
    suspend fun beginTurn(conversationId: String, userText: String, model: String?): TurnRefs

    /** 重试：不再插用户消息，只在上一条用户消息下挂一个新的助手兄弟。 */
    suspend fun beginRetry(conversationId: String, parentMessageId: String?, model: String?): TurnRefs

    fun appendText(textPartId: String, delta: String)

    fun appendReasoning(reasoningPartId: String, delta: String)

    fun recordUsage(conversationId: String, messageId: String, model: String?, counts: UsageCounts)

    fun finish(turn: TurnRefs, status: StoredStatus, finishReason: String?, errorJson: String?)

    /**
     * 软删一条消息（DDL `deleted_at`，回收站 30 天）：消息与它的整个子树一起打时间戳，
     * 清掉 FTS 行，重算会话的 `message_count` / `last_message_at`；活跃叶落在被删集合里时
     * 退回到被删消息的父节点。返回被标记的行数。
     */
    suspend fun softDeleteMessage(messageId: String): Int

    /** 等写队列排空；测试与"退出前落盘"用。 */
    suspend fun quiesce()
}
