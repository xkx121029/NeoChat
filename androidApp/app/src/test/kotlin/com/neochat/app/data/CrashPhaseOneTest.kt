package com.neochat.app.data

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import java.io.File
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assume.assumeTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

/**
 * 崩溃落盘的第一段（独立进程）：流式中途停下，不写终态。
 * 用两段 Gradle 进程跑同一个库文件，中间用 sqlite3 查状态，等价于"杀掉进程再打开"。
 */
@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class CrashPhaseOneTest {

    @Test
    fun `流式中途被强杀：库里留 streaming 与已提交正文`() {
        assumeTrue(System.getProperty("neochat.crash.phase") == PHASE)
        val context = ApplicationProvider.getApplicationContext<Context>()
        CrashHarness.clear(context)
        val db = NeoChatDatabase.openAt(context, CrashHarness.NAME)
        val store = RoomChatStore(db, now = { CLOCK })
        runBlocking {
            store.open()
            val conversation = store.createConversation()
            val turn = store.beginTurn(conversation, "重启后还认得这句话吗", MODEL)
            store.appendText(turn.textPartId, "第一段")
            store.appendText(turn.textPartId, "第二段")
            store.quiesce()
            println("phase1 conversation=$conversation assistant=${turn.assistantMessageId}")
        }
        // 模拟进程在此被强杀：不写终态、不 close，只把库文件搬出去
        CrashHarness.stash(context)
        assertEquals(true, File(CrashHarness.stableDir(), CrashHarness.NAME).exists())
    }

    private companion object {
        const val PHASE = "1"
        const val CLOCK = 1_800_000_100_000L
        const val MODEL = "deepseek-chat"
    }
}
