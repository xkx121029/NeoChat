package com.neochat.app.data

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import androidx.room.SkipQueryVerification
import kotlinx.coroutines.flow.Flow

/** 会话列表与软删。排序键与 DDL 的 `idx_conv_list` 一致；没有消息的会话按创建时间排。 */
@Dao
internal interface ConversationDao {

    @Query(
        "SELECT * FROM conversation WHERE deleted_at IS NULL " +
            "ORDER BY archived ASC, pinned DESC, COALESCE(last_message_at, created_at) DESC, id DESC",
    )
    fun observeAlive(): Flow<List<ConversationEntity>>

    @Query(
        "SELECT * FROM conversation WHERE deleted_at IS NULL " +
            "ORDER BY archived ASC, pinned DESC, COALESCE(last_message_at, created_at) DESC, id DESC LIMIT 1",
    )
    suspend fun mostRecent(): ConversationEntity?

    @Query("SELECT * FROM conversation WHERE id = :id AND deleted_at IS NULL")
    suspend fun find(id: String): ConversationEntity?

    @Insert(onConflict = OnConflictStrategy.ABORT)
    suspend fun insert(entity: ConversationEntity)

    @Query(
        "UPDATE conversation SET last_message_at = MAX(COALESCE(last_message_at, 0), :at), " +
            "message_count = message_count + :delta, updated_at = :at, " +
            "active_leaf_message_id = COALESCE(:leaf, active_leaf_message_id), " +
            "rev = :rev, sync_version = sync_version + 1 WHERE id = :id",
    )
    suspend fun touch(id: String, at: Long, delta: Long, leaf: String?, rev: String)

    /** 软删之后重算这一行的计数与活跃叶；`leaf` 为 null 表示整段分支都没了。 */
    @Query(
        "UPDATE conversation SET " +
            "active_leaf_message_id = :leaf, " +
            "last_message_at = (SELECT MAX(created_at) FROM message WHERE conversation_id = :id AND deleted_at IS NULL), " +
            "message_count = (SELECT COUNT(*) FROM message WHERE conversation_id = :id AND deleted_at IS NULL), " +
            "updated_at = :at, rev = :rev, sync_version = sync_version + 1 WHERE id = :id",
    )
    suspend fun retouchAfterDelete(id: String, leaf: String?, at: Long, rev: String)

    @Query("UPDATE conversation SET title = :title, updated_at = :at, rev = :rev, sync_version = sync_version + 1 WHERE id = :id AND title_auto = 1")
    suspend fun retitleIfAuto(id: String, title: String, at: Long, rev: String)

    @Query(
        "UPDATE conversation SET deleted_at = :at, updated_at = :at, rev = :rev, " +
            "sync_version = sync_version + 1 WHERE id = :id AND deleted_at IS NULL",
    )
    suspend fun softDelete(id: String, at: Long, rev: String)
}

/** 消息树：`parent_id` + `sibling_index` 决定分支，`status` 的四种取值见 DDL。 */
@Dao
internal interface MessageDao {

    @Insert(onConflict = OnConflictStrategy.ABORT)
    suspend fun insert(entity: MessageEntity)

    @Query("SELECT * FROM message WHERE conversation_id = :conversationId AND deleted_at IS NULL ORDER BY created_at ASC, sibling_index ASC")
    suspend fun listForConversation(conversationId: String): List<MessageEntity>

    @Query(
        "SELECT COUNT(*) FROM message WHERE conversation_id = :conversationId " +
            "AND parent_id IS :parentId AND deleted_at IS NULL",
    )
    suspend fun siblingCount(conversationId: String, parentId: String?): Long

    @Query(
        "UPDATE message SET status = :status, finish_reason = :finishReason, error_json = :errorJson, " +
            "updated_at = :at, rev = :rev, sync_version = sync_version + 1 WHERE id = :id",
    )
    suspend fun settle(id: String, status: String, finishReason: String?, errorJson: String?, at: Long, rev: String)

    @Query("""
        WITH RECURSIVE subtree(id) AS (
            SELECT id FROM message WHERE id = :id AND deleted_at IS NULL
            UNION ALL
            SELECT m.id FROM message m JOIN subtree s ON m.parent_id = s.id WHERE m.deleted_at IS NULL
        )
        SELECT id FROM subtree
    """)
    suspend fun subtreeIds(id: String): List<String>

    @Query("SELECT * FROM message WHERE id = :id")
    suspend fun find(id: String): MessageEntity?

    @Query(
        "UPDATE message SET deleted_at = :at, updated_at = :at, rev = :rev, " +
            "sync_version = sync_version + 1 WHERE id IN (:ids) AND deleted_at IS NULL",
    )
    suspend fun softDelete(ids: List<String>, at: Long, rev: String): Int

    /** FTS 维护契约（`v1.sql`）：只有活着的消息参与索引，软删同时清行。虚表 Room 校验不了。 */
    @SkipQueryVerification
    @Query("DELETE FROM message_fts WHERE message_id IN (:ids)")
    suspend fun deleteSearchRows(ids: List<String>)

    /** 进程被杀留下的 `streaming` 行；下次启动扫成 `aborted`，已收到的正文不动。 */
    @Query(
        "UPDATE message SET status = 'aborted', updated_at = :at, rev = :rev, " +
            "sync_version = sync_version + 1 WHERE status = 'streaming'",
    )
    suspend fun abortStreaming(at: Long, rev: String): Int
}

/** part 是流式落盘的最小单位：每个 delta 直接追加到 `body`。 */
@Dao
internal interface MessagePartDao {

    @Insert(onConflict = OnConflictStrategy.ABORT)
    suspend fun insert(entity: MessagePartEntity)

    @Query("UPDATE message_part SET body = COALESCE(body, '') || :delta, updated_at = :at WHERE id = :id")
    suspend fun append(id: String, delta: String, at: Long)

    @Query(
        "SELECT p.* FROM message_part p JOIN message m ON m.id = p.message_id " +
            "WHERE m.conversation_id = :conversationId AND m.deleted_at IS NULL " +
            "ORDER BY p.created_at ASC, p.part_index ASC",
    )
    suspend fun listForConversation(conversationId: String): List<MessagePartEntity>
}

/** 用量只追加不更新；本期不做统计界面，先保证数据在。 */
@Dao
internal interface UsageRecordDao {

    @Insert(onConflict = OnConflictStrategy.ABORT)
    suspend fun insert(entity: UsageRecordEntity)

    @Query("SELECT * FROM usage_record WHERE conversation_id = :conversationId ORDER BY created_at ASC")
    suspend fun listForConversation(conversationId: String): List<UsageRecordEntity>
}

/** 运行时设置页（本期只用到 `sync.device_id`）。 */
@Dao
internal interface AppSettingDao {

    @Query("SELECT value_json FROM app_setting WHERE key = :key")
    suspend fun value(key: String): String?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun put(entity: AppSettingEntity)
}
