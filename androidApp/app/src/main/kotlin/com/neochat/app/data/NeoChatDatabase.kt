package com.neochat.app.data

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import androidx.sqlite.db.SupportSQLiteDatabase
import com.neochat.app.R

/**
 * 二十二张实体表 + DDL 里的 FTS5 虚表。虚表 Room 声明不了，由 [SchemaCallback] 原样执行
 * `res/raw/schema_fts5.sql`（与 `spec/schema/v1.sql` 的 `CREATE VIRTUAL TABLE` 段落等价）。
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

        private fun build(builder: RoomDatabase.Builder<NeoChatDatabase>, context: Context): NeoChatDatabase =
            builder
                .setJournalMode(JournalMode.WRITE_AHEAD_LOGGING)
                .addCallback(SchemaCallback(context.applicationContext))
                .build()
    }
}

/**
 * 建库与每次开库的固定动作：建 FTS5 虚表、写初始行、按 DDL 头部要求设置 PRAGMA。
 * PRAGMA 用 `query` 执行——`execSQL` 会丢掉它们返回的结果行，个别 Android 版本因此不生效。
 */
internal class SchemaCallback(private val context: Context) : RoomDatabase.Callback() {

    private companion object {
        const val MISSING_FTS5 = "no such module: fts5"
    }

    override fun onCreate(db: SupportSQLiteDatabase) {
        createFullTextTable(db)
        db.execSQL("INSERT OR REPLACE INTO schema_meta(key, value) VALUES ('schema_version', ?)", arrayOf<Any?>(SchemaBaseline.VERSION.toString()))
        db.execSQL("INSERT OR REPLACE INTO schema_meta(key, value) VALUES ('created_at', ?)", arrayOf<Any?>(System.currentTimeMillis().toString()))
        db.execSQL(
            "INSERT OR REPLACE INTO app_setting(key, value_json, updated_at) VALUES ('ui.locale', '\"zh-CN\"', ?)",
            arrayOf<Any?>(System.currentTimeMillis()),
        )
        db.execSQL(
            "INSERT OR REPLACE INTO app_setting(key, value_json, updated_at) VALUES ('privacy.telemetry', 'false', ?)",
            arrayOf<Any?>(System.currentTimeMillis()),
        )
        val integrity = db.query("PRAGMA integrity_check").use { cursor ->
            if (cursor.moveToFirst()) cursor.getString(0) else "unknown"
        }
        db.execSQL(
            "INSERT OR REPLACE INTO migration(name, checksum, applied_at, snapshot_path, integrity_ok) VALUES (?, ?, ?, NULL, ?)",
            arrayOf<Any?>("v1.sql", SchemaBaseline.SPEC_SHA256, System.currentTimeMillis(), if (integrity == "ok") 1 else 0),
        )
    }

    /**
     * FTS5 是设备侧能力，不是 API 级别的承诺：AOSP API 33 模拟器的 SQLite 3.32.2 就没有编 FTS5
     * （`sqlite3` 直接报 `no such module: fts5`），Robolectric 也没有。
     * 因此这里对"缺模块"只放行不崩，`MessageIndex` 探测不到虚表时整条 FTS 路径静默跳过。
     */
    private fun createFullTextTable(db: SupportSQLiteDatabase) {
        val ddl = context.resources.openRawResource(R.raw.schema_fts5).bufferedReader().use { it.readText() }
        try {
            db.execSQL(ddl.trim().removeSuffix(";"))
        } catch (failure: RuntimeException) {
            if (failure.message?.contains(MISSING_FTS5) != true) throw failure
        }
    }

    override fun onOpen(db: SupportSQLiteDatabase) {
        // 外键只能走 Android 的开关：`PRAGMA foreign_keys` 被框架静默丢弃，查询时会读回 0。
        db.setForeignKeyConstraintsEnabled(true)
        db.query("PRAGMA synchronous = NORMAL").close()
        db.query("PRAGMA busy_timeout = 5000").close()
        db.query("PRAGMA temp_store = MEMORY").close()
    }
}
