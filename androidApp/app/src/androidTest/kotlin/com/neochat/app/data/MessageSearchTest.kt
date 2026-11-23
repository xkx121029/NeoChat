package com.neochat.app.data

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

/**
 * `message_fts` 的真机测试（Robolectric 的 SQLite 没编 FTS5，这条路径只能在设备上验）。
 *
 * 建 3 条中文消息，分别用 2 字词与 4 字词查询：命中集合与顺序都要对；
 * 软删其中一条后，只属于那条的词必须查不到，2 字词的命中集合也要少一条。
 */
@RunWith(AndroidJUnit4::class)
class MessageSearchTest {

    private lateinit var db: NeoChatDatabase
    private lateinit var store: RoomChatStore
    private var clock: Long = 1_800_000_000_000L

    private lateinit var conversationA: String
    private lateinit var conversationB: String
    private lateinit var conversationC: String
    private lateinit var messageA: String
    private lateinit var messageB: String
    private lateinit var messageC: String

    @Before
    fun setUp(): Unit = runBlocking {
        db = NeoChatDatabase.openInMemory(ApplicationProvider.getApplicationContext<Context>())
        store = RoomChatStore(db, now = { clock })
        store.open()
        conversationA = store.createConversation()
        messageA = store.beginTurn(conversationA, "帮我总结这篇文章要点", MODEL).userMessageId
        clock += 1_000
        conversationB = store.createConversation()
        messageB = store.beginTurn(conversationB, "再总结一下今天的会议", MODEL).userMessageId
        clock += 1_000
        conversationC = store.createConversation()
        messageC = store.beginTurn(conversationC, "把下面这段话改得更简洁", MODEL).userMessageId
        store.quiesce()
    }

    @After
    fun tearDown() {
        store.close()
        db.close()
    }

    @Test
    fun `两字词命中两条且按消息先后排序`(): Unit = runBlocking {
        val hits = store.search("总结")

        assertEquals(listOf(messageA, messageB), hits.map { it.messageId })
        assertEquals(listOf(conversationA, conversationB), hits.map { it.conversationId })
    }

    @Test
    fun `四字词只命中相邻的那一条`(): Unit = runBlocking {
        val hits = store.search("文章要点")

        assertEquals(listOf(messageA), hits.map { it.messageId })
        assertTrue(hits.single().snippet.contains("[文章"))
    }

    @Test
    fun `软删后不再命中_剩下的仍按顺序`(): Unit = runBlocking {
        store.softDeleteMessage(messageB)
        store.quiesce()

        assertEquals(listOf(messageA), store.search("总结").map { it.messageId })
        assertTrue(store.search("会议").isEmpty())
        assertEquals(listOf(messageC), store.search("简洁").map { it.messageId })
    }

    @Test
    fun `存活的text_part与FTS行一一对应`(): Unit = runBlocking {
        store.softDeleteMessage(messageC)
        store.quiesce()

        val parts = countOf(
            "SELECT COUNT(*) FROM message_part p JOIN message m ON m.id = p.message_id " +
                "WHERE p.kind = 'text' AND m.deleted_at IS NULL",
        )
        val rows = countOf("SELECT COUNT(*) FROM message_fts")

        assertEquals(parts, rows)
    }

    private fun countOf(sql: String): Int {
        db.openHelper.writableDatabase.query(sql).use { cursor ->
            assertTrue(cursor.moveToFirst())
            return cursor.getInt(0)
        }
    }

    private companion object {
        const val MODEL = "deepseek-chat"
    }
}
