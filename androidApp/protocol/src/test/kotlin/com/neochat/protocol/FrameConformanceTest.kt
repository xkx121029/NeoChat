package com.neochat.protocol

import com.neochat.protocol.errors.ErrorCode
import com.neochat.protocol.support.ChunkPattern
import com.neochat.protocol.support.Sse
import com.neochat.protocol.support.runSession
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/** spec/conformance/CASES.md §1：FRAME-001 ~ FRAME-011（012/013 在传输层用例里）。 */
class FrameConformanceTest {

    @Test
    fun `FRAME-001 中文 emoji 非BMP 按 bytes 1 喂入与整体喂入事件序列完全相同`() {
        val bytes = Sse.bytes(
            Sse.content("你好，世界"),
            Sse.content(" 🐱‍👤 emoji 😀"),
            Sse.content("非BMP：𝄞 𝕏"),
            Sse.finishOnly("stop"),
            Sse.done(),
        )
        val whole = runSession(bytes)
        val perByte = runSession(bytes, pattern = ChunkPattern.Bytes(1))
        println("FRAME-001 whole   = ${whole.lines}")
        println("FRAME-001 bytes:1 = ${perByte.lines}")
        assertEquals(whole.lines, perByte.lines)
        assertEquals("你好，世界 🐱‍👤 emoji 😀非BMP：𝄞 𝕏", whole.text)
        assertEquals(listOf("delta", "delta", "delta", "done"), whole.types)
    }

    @Test
    fun `FRAME-002 单个 data 事件切成 3 段且切点落在 JSON 字符串内部`() {
        val frame = Sse.content("hello world 你好")
        val bytes = Sse.bytes(frame, Sse.done())
        val cut1 = frame.indexOf("world")
        val cut2 = frame.indexOf("你好")
        val chunks = listOf(
            bytes.copyOfRange(0, cut1),
            bytes.copyOfRange(cut1, cut2 + 1),
            bytes.copyOfRange(cut2 + 1, bytes.size),
        )
        val session = StreamSession()
        chunks.forEach { session.feed(it) }
        session.endOfStream()
        val result = session.events()
        println("FRAME-002 events = ${result.map { it.toJsonLine() }}")
        assertEquals(1, result.filterIsInstance<StreamEvent.Delta>().size)
        assertEquals("hello world 你好", result.filterIsInstance<StreamEvent.Delta>().single().text)
    }

    @Test
    fun `FRAME-003 心跳注释不产生任何事件且被计数`() {
        val bytes = Sse.bytes(
            ": keep-alive\n",
            ": OPENROUTER PROCESSING\n",
            Sse.content("a"),
            ": keep-alive\n",
            ": keep-alive\n",
            Sse.content("b"),
            ": OPENROUTER PROCESSING\n",
            Sse.finishOnly("stop"),
            ": keep-alive\n",
            Sse.done(),
        )
        val result = runSession(bytes)
        println("FRAME-003 types=${result.types} trace=${result.trace}")
        assertEquals(listOf("delta", "delta", "done"), result.types)
        assertEquals("ab", result.text)
        assertEquals(6L, result.trace["comments_skipped"])
        assertTrue(result.events.none { it is StreamEvent.Notice })
    }

    @Test
    fun `FRAME-004 无 DONE 但末帧有 finish_reason stop 时 EOF 补 done`() {
        val bytes = Sse.bytes(Sse.content("hi"), Sse.finishOnly("stop"))
        val result = runSession(bytes)
        println("FRAME-004 events=${result.lines}")
        assertEquals(listOf("delta", "done"), result.types)
        assertEquals(FinishReason.STOP, result.done()?.finishReason)
        assertEquals(1L, result.trace["done_on_eof"])
    }

    @Test
    fun `FRAME-004b 无 DONE 且从未见 finish_reason 时 EOF 报 PARSE_INCOMPLETE`() {
        val bytes = Sse.bytes(Sse.content("hi"))
        val result = runSession(bytes)
        println("FRAME-004b events=${result.lines}")
        assertEquals(listOf("delta", "failed"), result.types)
        assertEquals(ErrorCode.PARSE_INCOMPLETE, result.failed()?.error?.code)
        assertEquals("2", result.failed()?.error?.params?.get("partial_text_length")?.let { Json.write(it) })
    }

    @Test
    fun `FRAME-005 DONE 出现在内容中途时后续字节不产生事件`() {
        val bytes = Sse.bytes(
            Sse.content("first"),
            Sse.finishOnly("stop"),
            Sse.done(),
            Sse.content("after-1"),
            Sse.content("after-2"),
            Sse.content("after-3"),
            Sse.done(),
        )
        val result = runSession(bytes, pattern = ChunkPattern.Bytes(3))
        println("FRAME-005 events=${result.lines}")
        assertEquals(listOf("delta", "done"), result.types)
        assertEquals("first", result.text)
        assertEquals(2, result.events.size)
    }

    @Test
    fun `FRAME-006 DONE 重复三次只有一个 done`() {
        val bytes = Sse.bytes(Sse.content("x"), Sse.done(), Sse.done(), Sse.done())
        val result = runSession(bytes)
        println("FRAME-006 events=${result.lines}")
        assertEquals(listOf("delta", "done"), result.types)
        assertEquals(1, result.events.count { it is StreamEvent.Done })
    }

    @Test
    fun `FRAME-007 混合 CRLF CR LF 行尾且载荷不含残留 CR`() {
        val frame1 = "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"A\"},\"finish_reason\":null}]}\r\n\r\n"
        val frame2 = "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"B\"},\"finish_reason\":null}]}\r\r"
        val frame3 = "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"C\"},\"finish_reason\":\"stop\"}]}\n\n"
        val bytes = Sse.bytes(frame1, frame2, frame3, "data: [DONE]\r\n\r\n")
        val result = runSession(bytes, pattern = ChunkPattern.Bytes(7))
        println("FRAME-007 events=${result.lines} text=${result.text}")
        assertEquals("ABC", result.text)
        assertFalse(result.text.contains('\r'))
        assertEquals(listOf("delta", "delta", "delta", "done"), result.types)
    }

    @Test
    fun `FRAME-008 首部 BOM 丢弃 中段非法 UTF-8 替换为 U+FFFD 并继续`() {
        val bom = byteArrayOf(0xEF.toByte(), 0xBB.toByte(), 0xBF.toByte())
        val head = "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"ab".toByteArray(Charsets.UTF_8)
        val invalid = byteArrayOf(0xFF.toByte(), 0xFE.toByte())
        val tail = "cd\"},\"finish_reason\":\"stop\"}]}\n\n".toByteArray(Charsets.UTF_8)
        val bytes = bom + head + invalid + tail + Sse.done().toByteArray(Charsets.UTF_8)
        val result = runSession(bytes, pattern = ChunkPattern.Bytes(1))
        println("FRAME-008 events=${result.lines} trace=${result.trace}")
        assertEquals("ab\uFFFD\uFFFDcd", result.text)
        assertEquals(2L, result.trace["invalid_utf8_bytes"])
        assertEquals(1L, result.trace["bom_stripped"])
        assertEquals(listOf("delta", "done"), result.types)
    }

    @Test
    fun `FRAME-009 单行 10 MiB 的 data 载荷被正常解析`() {
        val payload = "x".repeat(10 * 1024 * 1024)
        val bytes = Sse.bytes(Sse.content(payload), Sse.done())
        val session = StreamSession()
        var offset = 0
        val chunk = 64 * 1024
        while (offset < bytes.size) {
            val size = minOf(chunk, bytes.size - offset)
            session.feed(bytes, offset, size)
            offset += size
        }
        session.endOfStream()
        val deltas = session.events().filterIsInstance<StreamEvent.Delta>()
        println("FRAME-009 deltas=${deltas.size} textLength=${deltas.sumOf { it.text.length }} payloadBytes=${bytes.size}")
        assertEquals(1, deltas.size)
        assertEquals(payload.length, deltas.single().text.length)
        assertTrue(session.events().none { it is StreamEvent.Failed })
    }

    @Test
    fun `FRAME-010 单帧载荷 20 MiB 超限报 PARSE_EVENT_TOO_LARGE`() {
        val payload = "y".repeat(20 * 1024 * 1024)
        val bytes = Sse.bytes(Sse.content(payload), Sse.done())
        val session = StreamSession()
        var offset = 0
        val chunk = 64 * 1024
        while (offset < bytes.size && !session.isTerminated()) {
            val size = minOf(chunk, bytes.size - offset)
            session.feed(bytes, offset, size)
            offset += size
        }
        val failed = session.events().filterIsInstance<StreamEvent.Failed>().firstOrNull()
        println("FRAME-010 failed=${failed?.toJsonLine()}")
        assertNotNull(failed)
        assertEquals(ErrorCode.PARSE_EVENT_TOO_LARGE, failed?.error?.code)
        assertTrue(session.isTerminated())
    }

    @Test
    fun `FRAME-011 空 delta 不产生事件 I6`() {
        val bytes = Sse.bytes(
            Sse.content(""),
            Sse.content("A"),
            Sse.content(""),
            Sse.content("B"),
            Sse.content(""),
            Sse.finishOnly("stop"),
            Sse.done(),
        )
        val result = runSession(bytes)
        println("FRAME-011 events=${result.lines}")
        assertEquals(2, result.deltas().size)
        assertEquals("AB", result.text)
        assertEquals(listOf("delta", "delta", "done"), result.types)
    }
}
