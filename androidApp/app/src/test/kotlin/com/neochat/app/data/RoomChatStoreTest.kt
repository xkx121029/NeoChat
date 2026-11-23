package com.neochat.app.data

import android.content.Context
import android.database.Cursor
import androidx.sqlite.db.SupportSQLiteDatabase
import androidx.test.core.app.ApplicationProvider
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

/** 真 SQLite：Room 在 Robolectric 下建的是 Android 的那套 SQLite，不是内存假实现。 */
@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class RoomChatStoreTest {

    private lateinit var db: NeoChatDatabase
    private lateinit var store: RoomChatStore
    private var clock: Long = 1_800_000_000_000L

    @Before
    fun setUp() {
        db = NeoChatDatabase.openInMemory(ApplicationProvider.getApplicationContext<Context>())
        store = RoomChatStore(db, now = { clock })
    }

    @After
    fun tearDown() {
        store.close()
        db.close()
    }

    @Test
    fun `每收到一个 delta 就落库，流没结束库里已有正文`(): Unit = runBlocking {
        store.open()
        val conversation = store.createConversation()
        val turn = store.beginTurn(conversation, "你好", MODEL)

        store.appendText(turn.textPartId, "你")
        store.appendText(turn.textPartId, "好")
        store.quiesce()

        val messages = store.loadMessages(conversation)
        assertEquals(2, messages.size)
        assertEquals(StoredRole.USER, messages[0].role)
        assertEquals("你好", messages[0].text)
        assertEquals(StoredStatus.STREAMING, messages[1].status)
        assertEquals("你好", messages[1].text)
    }

    @Test
    fun `重启扫残留：streaming 变 aborted，已收正文一词不少`(): Unit = runBlocking {
        store.open()
        val conversation = store.createConversation()
        val turn = store.beginTurn(conversation, "你好", MODEL)
        store.appendText(turn.textPartId, "半句")
        store.appendReasoning(turn.reasoningPartId, "想一想")
        store.quiesce()

        val restarted = RoomChatStore(db, now = { clock })
        val snapshot = restarted.open()

        assertEquals(1, snapshot.sweptStreaming)
        val messages = restarted.loadMessages(conversation)
        assertEquals(StoredStatus.ABORTED, messages[1].status)
        assertEquals("半句", messages[1].text)
        assertEquals("想一想", messages[1].reasoning)
        restarted.close()
    }

    @Test
    fun `终态落 status 与 finish_reason，活跃叶指向助手消息`(): Unit = runBlocking {
        store.open()
        val conversation = store.createConversation()
        val turn = store.beginTurn(conversation, "你好", MODEL)
        store.appendText(turn.textPartId, "答案")
        clock += 5
        store.finish(turn, StoredStatus.COMPLETE, "stop", null)
        store.quiesce()

        val messages = store.loadMessages(conversation)
        assertEquals(StoredStatus.COMPLETE, messages[1].status)
        val settled = readOne(
            "SELECT m.finish_reason, c.active_leaf_message_id FROM message m " +
                "JOIN conversation c ON c.id = m.conversation_id WHERE m.id = ?",
            arrayOf(turn.assistantMessageId),
        ) { cursor -> cursor.getString(0) to cursor.getString(1) }
        assertEquals("stop", settled.first)
        assertEquals(turn.assistantMessageId, settled.second)
    }

    @Test
    fun `会话列表按 last_message_at 倒序`(): Unit = runBlocking {
        store.open()
        val first = store.createConversation()
        store.beginTurn(first, "第一句", MODEL)
        clock += 1_000
        val second = store.createConversation()
        clock += 1_000
        store.beginTurn(second, "第二句", MODEL)

        val list = store.conversations().first()

        assertEquals(listOf(second, first), list.map { it.id })
        assertEquals("第二句", list[0].title)
    }

    @Test
    fun `软删只改 deleted_at：列表里没了，行还在`(): Unit = runBlocking {
        store.open()
        val conversation = store.createConversation()
        store.beginTurn(conversation, "你好", MODEL)

        store.deleteConversation(conversation)

        val list = store.conversations().first()
        assertTrue(list.none { it.id == conversation })
        val deletedAt = readOne("SELECT deleted_at FROM conversation WHERE id = ?", arrayOf(conversation)) {
            it.getLong(0)
        }
        assertEquals(clock, deletedAt)
    }

    @Test
    fun `重试挂兄弟分支，读回只走活跃叶`(): Unit = runBlocking {
        store.open()
        val conversation = store.createConversation()
        val turn = store.beginTurn(conversation, "你好", MODEL)
        store.appendText(turn.textPartId, "第一版")
        store.finish(turn, StoredStatus.COMPLETE, "stop", null)
        store.quiesce()

        clock += 10
        val retry = store.beginRetry(conversation, turn.userMessageId, MODEL)
        store.appendText(retry.textPartId, "第二版")
        store.finish(retry, StoredStatus.COMPLETE, "stop", null)
        store.quiesce()

        val branch = store.loadMessages(conversation)
        assertEquals(2, branch.size)
        assertEquals("第二版", branch[1].text)
        val siblings = readOne(
            "SELECT COUNT(*) FROM message WHERE parent_id = ? AND deleted_at IS NULL",
            arrayOf(turn.userMessageId),
        ) { it.getInt(0) }
        assertEquals(2, siblings)
    }

    @Test
    fun `首条用户消息折叠空白后成为标题`(): Unit = runBlocking {
        store.open()
        val conversation = store.createConversation()
        store.beginTurn(conversation, "  你好\n\n世界  ", MODEL)
        store.beginTurn(conversation, "这条不该改标题", MODEL)

        val summary = store.conversations().first().single { it.id == conversation }

        assertEquals("你好 世界", summary.title)
    }

    @Test
    fun `schema_meta、migration 与设备 id 在建库时写好`(): Unit = runBlocking {
        store.open()
        val version = readOne("SELECT value FROM schema_meta WHERE key = 'schema_version'") { it.getString(0) }
        val migration = readOne("SELECT checksum, integrity_ok FROM migration") {
            it.getString(0) to it.getInt(1)
        }
        val device = readOne("SELECT value_json FROM app_setting WHERE key = 'sync.device_id'") {
            it.getString(0)
        }
        assertEquals("1", version)
        assertEquals(SchemaBaseline.SPEC_SHA256, migration.first)
        assertEquals(1, migration.second)
        assertEquals(38, device.length)
    }

    /** Room 2.8 的 `query` 只收 SupportSQLiteQuery，这里直接走 openHelper，并挪到 IO 线程。 */
    private suspend fun <T> readOne(
        sql: String,
        args: Array<Any?> = emptyArray(),
        read: (Cursor) -> T,
    ): T = withContext(Dispatchers.IO) {
        val database: SupportSQLiteDatabase = db.openHelper.writableDatabase
        database.query(sql, args).use { cursor ->
            assertTrue(cursor.moveToFirst())
            read(cursor)
        }
    }

    private companion object {
        const val MODEL = "deepseek-chat"
    }
}
