package com.neochat.app.data

import android.content.Context
import java.io.File

/**
 * 两段进程之间搬库文件：Robolectric 的 `getDatabasePath` 只接受相对名字（传绝对路径会在
 * shadow 里抛 `StringIndexOutOfBoundsException`），所以库里文件落在临时目录，测完再复制到
 * `-Dneochat.crash.db` 指定的稳定目录，下一段进程复制回去再打开。
 */
internal object CrashHarness {

    const val NAME: String = "neochat-crash.db"

    private val suffixes = listOf("", "-wal", "-shm")

    fun stableDir(): File = File(requireNotNull(System.getProperty("neochat.crash.db"))).also { it.mkdirs() }

    fun localFile(context: Context, suffix: String): File = File(context.getDatabasePath(NAME).absolutePath + suffix)

    fun clear(context: Context) {
        suffixes.forEach { localFile(context, it).delete() }
        suffixes.forEach { File(stableDir(), NAME + it).delete() }
    }

    /** 第一段进程收尾：把 db / -wal / -shm 原样搬走（不 checkpoint，保留进程被杀时的现场）。 */
    fun stash(context: Context) {
        suffixes.forEach { File(stableDir(), NAME + it).delete() }
        suffixes.forEach { suffix ->
            val source = localFile(context, suffix)
            if (source.exists()) source.copyTo(File(stableDir(), NAME + suffix), overwrite = true)
        }
    }

    /** 第二段进程开库前：把现场搬回 Robolectric 的库目录。 */
    fun restore(context: Context) {
        suffixes.forEach { suffix ->
            val source = File(stableDir(), NAME + suffix)
            if (source.exists()) source.copyTo(localFile(context, suffix), overwrite = true)
        }
    }
}
