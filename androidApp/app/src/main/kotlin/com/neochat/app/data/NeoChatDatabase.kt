package com.neochat.app.data

import android.content.Context
import android.os.Build
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import androidx.sqlite.SQLiteConnection
import androidx.sqlite.SQLiteStatement
import androidx.sqlite.execSQL
import com.neochat.app.R

/**
 * 二十二张实体表 + DDL 里的 FTS5 虚表。虚表 Room 声明不了，由 [SchemaCallback] 原样执行
 * `res/raw/schema_fts5.sql`（与 `spec/schema/v1.sql` 的 `CREATE VIRTUAL TABLE` 段落等价）。
 *
 * 引擎是随 APK 分发的 SQLite（[BundledSqlite]，ADR-004），不是系统的那个：系统库有没有 FTS5
 * 由 ROM 决定。换成驱动后 Room 的回调收到 [SQLiteConnection] 而不是 `SupportSQLiteDatabase`，
 * `openHelper` 与 `androidx.room.withTransaction` 都不再可用，事务要按连接写（见 `RoomChatStore`）。
 */
@Database(
    entities = [
        AgentProfileEntity::class,
        AppSettingEntity::class,
        AttachmentEntity::class,
        ConversationEntity::class,
        ConversationTagEntity::class,
        FolderEntity::class,
        McpServerEntity::class,
        McpToolEntity::class,
        MemoryItemEntity::class,
        MessageEntity::class,
        MessageAttachmentEntity::class,
        MessagePartEntity::class,
        MigrationEntity::class,
        ModelEntity::class,
        PromptTemplateEntity::class,
        ProviderEntity::class,
        ProviderKeyEntity::class,
        SchemaMetaEntity::class,
        TagEntity::class,
        TombstoneEntity::class,
        ToolCallRecordEntity::class,
        UsageRecordEntity::class,
    ],
    version = SchemaBaseline.VERSION,
    exportSchema = true,
)
internal abstract class NeoChatDatabase : RoomDatabase() {

    abstract fun conversations(): ConversationDao

    abstract fun messages(): MessageDao

    abstract fun parts(): MessagePartDao

    abstract fun usage(): UsageRecordDao

    abstract fun settings(): AppSettingDao

    companion object {

        const val NAME: String = "neochat.db"

        fun open(context: Context): NeoChatDatabase = build(
            Room.databaseBuilder(context, NeoChatDatabase::class.java, NAME),
            context,
        )

        /** 指定库文件名（绝对路径也可）：崩溃测试要在两次进程之间复用同一个文件。 */
        fun openAt(context: Context, name: String): NeoChatDatabase = build(
            Room.databaseBuilder(context, NeoChatDatabase::class.java, name),
            context,
        )

        fun openInMemory(context: Context): NeoChatDatabase = build(
            Room.inMemoryDatabaseBuilder(context, NeoChatDatabase::class.java),
            context,
        )

        /**
         * Robolectric 在宿主 JVM 上跑，自带 SQLite 的 .so 是给 Android ABI 编的，装不进来；
         * 那里的单测退回平台 SQLite（没有 FTS5，`MessageIndex` 会探测到并跳过 FTS）。真机与模拟器
         * 一律走自带引擎——这才是 ADR-004 要保证的东西。
         */
        private val hostedUnitTest: Boolean
            get() = Build.FINGERPRINT.equals("robolectric", ignoreCase = true)

        private fun build(builder: RoomDatabase.Builder<NeoChatDatabase>, context: Context): NeoChatDatabase =
            builder
                .apply { if (!hostedUnitTest) setDriver(BundledSqlite.driver) }
                .setJournalMode(JournalMode.WRITE_AHEAD_LOGGING)
                .addCallback(SchemaCallback(context.applicationContext, bundled = !hostedUnitTest))
                .build()
    }
}

/**
 * 建库与开库的固定动作：建 FTS5 虚表、写初始行、按 DDL 头部要求设置 PRAGMA。
 *
 * 只实现收 [SQLiteConnection] 的那两个回调：驱动路径调它们，平台 SQLite 路径（宿主 JVM 单测）
 * 也调它们——Room 把 `SupportSQLiteDatabase` 包成 `SupportSQLiteConnection` 传进来。
 */
internal class SchemaCallback(
    private val context: Context,
    private val bundled: Boolean,
) : RoomDatabase.Callback() {

    override fun onCreate(connection: SQLiteConnection) {
        createFullTextTable(connection)
        val at = System.currentTimeMillis()
        connection.execute(
            "INSERT OR REPLACE INTO schema_meta(key, value) VALUES ('schema_version', ?)",
            SchemaBaseline.VERSION.toString(),
        )
        connection.execute(
            "INSERT OR REPLACE INTO schema_meta(key, value) VALUES ('created_at', ?)",
            at.toString(),
        )
        connection.execute(
            "INSERT OR REPLACE INTO app_setting(key, value_json, updated_at) VALUES ('ui.locale', '\"zh-CN\"', ?)",
            at,
        )
        connection.execute(
            "INSERT OR REPLACE INTO app_setting(key, value_json, updated_at) VALUES ('privacy.telemetry', 'false', ?)",
            at,
        )
        val integrity = connection.textOf("PRAGMA integrity_check")
        connection.execute(
            "INSERT OR REPLACE INTO migration(name, checksum, applied_at, snapshot_path, integrity_ok) " +
                "VALUES (?, ?, ?, NULL, ?)",
            "v1.sql",
            SchemaBaseline.SPEC_SHA256,
            at,
            if (integrity == "ok") 1 else 0,
        )
    }

    /**
     * 每次开库都补一次虚表：`CREATE VIRTUAL TABLE IF NOT EXISTS` 是幂等的，而换引擎之前建出来的
     * 老库（系统 SQLite 没有 FTS5）里是没有这张表的，只靠 onCreate 会一直缺下去。
     */
    override fun onOpen(connection: SQLiteConnection) {
        createFullTextTable(connection)
        BundledSqlite.PRAGMAS.forEach { connection.execute(it) }
    }

    /**
     * 自带引擎必带 FTS5：建不出来就是缺陷，直接抛。宿主 JVM 单测的平台 SQLite 没编 FTS5，
     * 那条路径上放过"缺模块"，`MessageIndex` 会探测到并跳过 FTS。
     */
    private fun createFullTextTable(connection: SQLiteConnection) {
        val ddl = context.resources.openRawResource(R.raw.schema_fts5).bufferedReader().use { it.readText() }
        try {
            connection.execSQL(ddl.trim().removeSuffix(";"))
        } catch (failure: RuntimeException) {
            val missing = failure.message?.contains(MISSING_FTS5) == true
            if (bundled || !missing) throw failure
        }
    }

    private companion object {
        const val MISSING_FTS5 = "no such module: fts5"
    }
}

/** 按绑定值类型分派；SQLite 驱动不给 `execSQL` 的绑定重载，只有 [SQLiteStatement]。 */
internal fun SQLiteConnection.execute(sql: String, vararg args: Any?) {
    prepare(sql).use { statement ->
        args.forEachIndexed { offset, value ->
            val index = offset + 1
            when (value) {
                null -> statement.bindNull(index)
                is Int -> statement.bindInt(index, value)
                is Long -> statement.bindLong(index, value)
                else -> statement.bindText(index, value.toString())
            }
        }
        statement.step()
    }
}

/** 读一行的第一列（如 `PRAGMA integrity_check`）；没有结果行时给 `unknown`。 */
internal fun SQLiteConnection.textOf(sql: String): String =
    prepare(sql).use { statement -> if (statement.step()) statement.getText(0) else "unknown" }
