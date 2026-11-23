package com.neochat.app.data

import android.content.Context
import android.database.Cursor
import androidx.sqlite.db.SupportSQLiteDatabase
import androidx.test.core.app.ApplicationProvider
import com.neochat.protocol.Json
import com.neochat.protocol.JsonValue
import java.io.File
import java.security.MessageDigest
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import androidx.room.withTransaction
import kotlinx.coroutines.withContext
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

/**
 * 把 Room 真建出来的库 dump 成快照，与 `androidApp/schema/room-v1.json` 逐表逐列比对；
 * 那份 JSON 又由 `tools/schema-diff/diff_schema.py` 与 `spec/schema/v1.sql` 比对。
 * 两侧都绿，等于 Room 的列与冻结 DDL 一致。
 */
@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class SchemaSnapshotTest {

    /**
     * Robolectric 自带的 SQLite 没编 FTS5，`message_fts` 及其影子表在这条路径上不存在，
     * 因此运行时比对覆盖 22 张实体表；虚表的列一致性由 room-v1.json（Python 侧带 FTS5）与
     * spec/schema/v1.sql 的 diff 覆盖。
     */
    @Test
    fun `Room 建出的库与 room-v1 快照逐表逐列相等`(): Unit = runBlocking {
        val actual = dump(open())
        val expected = Json.parse(File(requireNotNull(System.getProperty("neochat.schema.room"))).readText())
        assertEquals(entityTables(denormalize(expected)), entityTables(actual))
        assertEquals(22, (actual.getValue("tables") as Map<*, *>).size)
    }

    /**
     * SQLite 的 synchronous 编号：1 = NORMAL、2 = FULL；temp_store：2 = MEMORY。
     * `foreign_keys` 走 `setForeignKeyConstraintsEnabled(true)`（PRAGMA 会被 Android 丢弃），
     * Robolectric 的 SQLite shadow 不实现该开关，读回恒为 0，因此这里不断言它。
     */
    @Test
    fun `PRAGMA 按 DDL 头部要求设置`(): Unit = runBlocking {
        val context = ApplicationProvider.getApplicationContext<Context>()
        context.deleteDatabase(PRAGMA_DB)
        val db = NeoChatDatabase.openAt(context, PRAGMA_DB)
        assertEquals("wal", pragma(db, "PRAGMA journal_mode"))
        assertEquals("1", pragma(db, "PRAGMA synchronous"))
        assertEquals("5000", pragma(db, "PRAGMA busy_timeout"))
        assertEquals("2", pragma(db, "PRAGMA temp_store"))
        db.close()
        context.deleteDatabase(PRAGMA_DB)
    }

    @Test
    fun `SPEC_SHA256 与契约文件一致`() {
        val path = requireNotNull(System.getProperty("neochat.spec.sql"))
        val digest = MessageDigest.getInstance("SHA-256").digest(File(path).readBytes())
        assertEquals(SchemaBaseline.SPEC_SHA256, digest.joinToString("") { "%02x".format(it) })
    }

    private fun open(): NeoChatDatabase =
        NeoChatDatabase.openInMemory(ApplicationProvider.getApplicationContext<Context>())

    /** onOpen 里的 PRAGMA 只对主连接生效，所以读回也要在主连接上做——包一个事务即可。 */
    private suspend fun pragma(db: NeoChatDatabase, statement: String): String = db.withTransaction {
        database(db).query(statement).use { cursor ->
            assertTrue(cursor.moveToFirst())
            cursor.getString(0)
        }
    }

    private suspend fun dump(db: NeoChatDatabase): Map<String, Any?> = withContext(Dispatchers.IO) {
        val tables = sortedMapOf<String, Any?>()
        database(db).query("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name")
            .use { cursor ->
                while (cursor.moveToNext()) {
                    val table = cursor.getString(0)
                    if (table in INFRASTRUCTURE) continue
                    tables[table] = mapOf("columns" to dumpColumns(database(db), table))
                }
            }
        db.close()
        mapOf("format" to SNAPSHOT_FORMAT, "tables" to tables)
    }

    private fun dumpColumns(database: SupportSQLiteDatabase, table: String): Map<String, Any?> {
        val columns = sortedMapOf<String, Any?>()
        database.query("PRAGMA table_info(`$table`)").use { cursor ->
            while (cursor.moveToNext()) {
                columns[cursor.getString(1)] = column(cursor)
            }
        }
        return columns
    }

    private fun column(cursor: Cursor): Map<String, Any?> = mapOf(
        "type" to cursor.getString(2).uppercase(),
        "notnull" to (cursor.getInt(3) != 0),
        "pk" to (cursor.getInt(5) != 0),
        "default" to if (cursor.isNull(4)) null else cursor.getString(4),
    )

    private fun database(db: NeoChatDatabase): SupportSQLiteDatabase = db.openHelper.writableDatabase

    @Suppress("UNCHECKED_CAST")
    private fun entityTables(snapshot: Any?): Map<String, Any?> {
        val tables = (snapshot as Map<String, Any?>).getValue("tables") as Map<String, Any?>
        return tables.filterKeys { !it.startsWith("message_fts") }
    }

    /** 协议层 JSON → 纯 Map/List，便于与 dump 结果直接断言。 */
    private fun denormalize(value: JsonValue): Any? = when (value) {
        is JsonValue.Obj -> value.fields.mapValues { denormalize(it.value) }
        is JsonValue.Arr -> value.items.map { denormalize(it) }
        is JsonValue.Str -> value.value
        is JsonValue.Num -> value.raw
        is JsonValue.Bool -> value.value
        JsonValue.Null -> null
    }

    private companion object {
        const val SNAPSHOT_FORMAT = "neochat-schema-snapshot/1"
        const val PRAGMA_DB = "neochat-pragma-test.db"
        val INFRASTRUCTURE = setOf("android_metadata", "room_master_table")
    }
}
