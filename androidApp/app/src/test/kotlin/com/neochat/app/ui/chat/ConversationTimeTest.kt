package com.neochat.app.ui.chat

import java.time.ZoneOffset
import java.time.ZonedDateTime
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test

/** 列表时间分档：今天 / 昨天 / 今年 / 更早，四档互斥。 */
class ConversationTimeTest {

    private val zone = ZoneOffset.ofHours(8)

    private fun at(year: Int, month: Int, day: Int, hour: Int, minute: Int): Long =
        ZonedDateTime.of(year, month, day, hour, minute, 0, 0, zone).toInstant().toEpochMilli()

    @Test
    fun `同一天显示时分`() {
        val now = at(2026, 11, 23, 9, 30)
        val time = ConversationTime.of(at(2026, 11, 23, 8, 5), now, zone)
        assertEquals(ConversationTime.Today(8, 5), time)
    }

    @Test
    fun `前一天显示昨天`() {
        val now = at(2026, 11, 23, 9, 30)
        val time = ConversationTime.of(at(2026, 11, 22, 23, 59), now, zone)
        assertEquals(ConversationTime.Yesterday, time)
    }

    @Test
    fun `同年显示月日`() {
        val now = at(2026, 11, 23, 9, 30)
        val time = ConversationTime.of(at(2026, 3, 8, 12, 0), now, zone)
        assertEquals(ConversationTime.SameYear(3, 8), time)
    }

    @Test
    fun `跨年显示年月日`() {
        val now = at(2026, 11, 23, 9, 30)
        val time = ConversationTime.of(at(2025, 12, 31, 23, 0), now, zone)
        assertEquals(ConversationTime.Older(2025, 12, 31), time)
    }
}
