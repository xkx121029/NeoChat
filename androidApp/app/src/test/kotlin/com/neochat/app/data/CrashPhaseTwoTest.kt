package com.neochat.app.data

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assume.assumeTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

/** 崩溃落盘的第二段：重启后打开同一个库文件，残留 streaming 被扫成 aborted，正文不动。 */
@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class CrashPhaseTwoTest {

    @Test
    fun `重启扫描把残留 streaming 落成 aborted，正文一字不丢`() {
        assumeTrue(System.getProperty("neochat.crash.phase") == PHASE)
        val context = ApplicationProvider.getApplicationContext<Context>()
        CrashHarness.restore(context)
        val db = NeoChatDatabase.openAt(context, CrashHarness.NAME)
        val store = RoomChatStore(db, now = { CLOCK })
        runBlocking {
            val snapshot = store.open()
            assertEquals(1, snapshot.sweptStreaming)
            val conversation = requireNotNull(snapshot.latestConversationId)
            val messages = store.loadMessages(conversation)
            assertEquals(2, messages.size)
            assertEquals(StoredStatus.ABORTED, messages[1].status)
            assertEquals("第一段第二段", messages[1].text)
            println("phase2 conversation=$conversation status=${messages[1].status.wire} text=${messages[1].text}")
        }
        store.close()
        db.close()
        CrashHarness.stash(context)
    }

    private companion object {
        const val PHASE = "2"
        const val CLOCK = 1_800_000_200_000L
    }
}
