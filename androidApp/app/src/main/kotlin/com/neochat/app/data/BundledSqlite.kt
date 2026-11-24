package com.neochat.app.data

import androidx.sqlite.SQLiteConnection
import androidx.sqlite.SQLiteDriver
import androidx.sqlite.driver.bundled.BundledSQLiteDriver

/**
 * 随 APK 分发的 SQLite（ADR-004 §2.1）。设备上的系统 SQLite 由 ROM 编译决定有没有 FTS5：
 * AOSP API 33 的 3.32.2 就没有（`no such module: fts5`），所以"能不能搜"不能交给 ROM。
 *
 * 五条 PRAGMA（`spec/schema/v1.sql` 头部）钉在**每条连接**开出来的时刻：Room 按 [SQLiteDriver]
 * 自己开读连接，只在 onOpen 设一次管不到后来者，`foreign_keys` 也必须逐连接设置。
 */
internal object BundledSqlite {

    val driver: SQLiteDriver = PragmaDriver(BundledSQLiteDriver())

    private class PragmaDriver(private val delegate: SQLiteDriver) : SQLiteDriver {

        override val hasConnectionPool: Boolean
            get() = delegate.hasConnectionPool

        override fun open(fileName: String): SQLiteConnection = delegate.open(fileName).also { connection ->
            PRAGMAS.forEach { sql ->
                connection.prepare(sql).use { statement -> statement.step() }
            }
        }
    }

    val PRAGMAS: List<String> = listOf(
        "PRAGMA journal_mode = WAL",
        "PRAGMA foreign_keys = ON",
        "PRAGMA synchronous = NORMAL",
        "PRAGMA busy_timeout = 5000",
        "PRAGMA temp_store = MEMORY",
    )
}
