package com.neochat.app.data

/**
 * `message_fts` 的维护者：一个 text part 一行，正文变了就删旧行插新行。
 *
 * 写入/删除不自己开事务——调用方（[RoomChatStore]）把"改 part + 改 FTS 行"包在同一个事务里，
 * 保证 `v1.sql` 头部注释要求的原子性。
 */
internal class MessageIndex(private val db: NeoChatDatabase) {

    /**
     * 只在第一次用时探测。设备上是自带 SQLite，FTS5 必在（ADR-004），这里恒为 true；
     * 宿主 JVM 单测跑的是 Robolectric 的平台 SQLite，没有 FTS5，那里整条 FTS 路径跳过。
     */
    private var available: Boolean? = null

    /** 把某个 part 的当前正文同步成一行；非 text part 或 part 已不存在时只做清理。 */
    suspend fun write(partId: String) {
        if (!ready()) return
        val target = db.parts().indexPath(partId) ?: return
        db.messages().deleteSearchRow(target.messageId, target.partIndex)
        if (target.kind != KIND_TEXT) return
        db.messages().insertSearchRow(
            text = SearchText.tokens(target.body.orEmpty()),
            messageId = target.messageId,
            conversationId = target.conversationId,
            partIndex = target.partIndex,
        )
    }

    /** 软删消息时清掉整棵子树的 FTS 行。 */
    suspend fun remove(ids: List<String>) {
        if (!ready() || ids.isEmpty()) return
        db.messages().deleteSearchRows(ids)
    }

    /** 查询串必须已经由 [SearchText.matchExpression] 转成 FTS5 表达式。 */
    suspend fun search(expression: String): List<SearchHit> {
        if (!ready()) return emptyList()
        return db.messages().search(expression).map { row ->
            SearchHit(
                messageId = row.messageId,
                conversationId = row.conversationId,
                partIndex = row.partIndex,
                snippet = row.snippet,
            )
        }
    }

    private suspend fun ready(): Boolean {
        available?.let { return it }
        val probe = db.messages().searchTableCount() > 0
        available = probe
        return probe
    }

    private companion object {
        const val KIND_TEXT = "text"
    }
}
