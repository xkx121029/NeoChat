package com.neochat.app.data

import android.content.Context
import android.util.Log
import androidx.room.useWriterConnection
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

/**
 * 设备侧的引擎体检（ADR-004）：证明"用的是随 APK 带进来的 SQLite，而不是 ROM 那个"。
 *
 * 写的是文件库（`NeoChatDatabase.open`，即应用真实使用的 `neochat.db`）而不是内存库，
 * 这样 `adb shell run-as com.neochat.app` 事后查的就是同一份数据。
 */
@RunWith(AndroidJUnit4::class)
class DeviceDatabaseTest {

    private val context: Context = ApplicationProvider.getApplicationContext()

    @Test
    fun `自带引擎带FTS5_五条PRAGMA生效_写入落在message_fts`(): Unit = runBlocking {
        val db = NeoChatDatabase.open(context)
        val store = RoomChatStore(db)
        store.open()
        val conversation = store.createConversation()
        store.beginTurn(conversation, "自带 SQLite 的 FTS5 探针", MODEL)
        store.quiesce()

        val version = read(db, "SELECT sqlite_version()")
        val journal = read(db, "PRAGMA journal_mode")
        val foreignKeys = read(db, "PRAGMA foreign_keys")
        val synchronous = read(db, "PRAGMA synchronous")
        val busyTimeout = read(db, "PRAGMA busy_timeout")
        val tempStore = read(db, "PRAGMA temp_store")
        val ftsRows = read(db, "SELECT COUNT(*) FROM message_fts")
        val hits = read(db, "SELECT COUNT(*) FROM message_fts WHERE message_fts MATCH 'SQLite'")
        val matches = store.search("SQLite")

        Log.i(
            TAG,
            "sqlite=$version journal=$journal foreign_keys=$foreignKeys synchronous=$synchronous " +
                "busy_timeout=$busyTimeout temp_store=$tempStore fts_rows=$ftsRows match=$hits",
        )

        assertTrue("自带引擎版本应高于 ROM 的 3.32.2，实际 $version", rank(version) > rank("3.32.2"))
        assertEquals("wal", journal)
        assertEquals("1", foreignKeys)
        assertEquals("1", synchronous)
        assertEquals("5000", busyTimeout)
        assertEquals("2", tempStore)
        assertTrue("message_fts 应有行，实际 $ftsRows", ftsRows.toInt() >= 1)
        assertTrue("MATCH 'SQLite' 应命中，实际 $hits", hits.toInt() >= 1)
        assertTrue("新会话应出现在命中里", conversation in matches.map { it.conversationId })

        store.close()
        db.close()
    }

    /** 读一行的第一列；`PRAGMA` 也必须真读结果行，否则某些实现在语句执行后不留值。 */
    private suspend fun read(db: NeoChatDatabase, sql: String): String = db.useWriterConnection { connection ->
        connection.usePrepared(sql) { prepared -> if (prepared.step()) prepared.getText(0) else "unknown" }
    }

    /** "3.50.4" 与 "3.32.2" 按 主*1e6 + 次*1e3 + 修订 比大小。 */
    private fun rank(version: String): Int {
        val parts = version.split(".").mapNotNull { it.takeWhile(Char::isDigit).toIntOrNull() }
        return (parts.getOrNull(0) ?: 0) * 1_000_000 +
            (parts.getOrNull(1) ?: 0) * 1_000 +
            (parts.getOrNull(2) ?: 0)
    }

    private companion object {
        const val MODEL = "deepseek-chat"
        const val TAG = "NeoChatSqlite"
    }
}
