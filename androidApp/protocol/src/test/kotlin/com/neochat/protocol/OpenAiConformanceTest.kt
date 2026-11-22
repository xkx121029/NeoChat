package com.neochat.protocol

import com.neochat.protocol.support.ChunkPattern
import com.neochat.protocol.support.RunResult
import com.neochat.protocol.support.Sse
import com.neochat.protocol.support.runSession
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/** spec/conformance/CASES.md §2：OAI-001 ~ OAI-012（OAI-007 是 HTTP 层，见 TransportConformanceTest）。 */
class OpenAiConformanceTest {

    private fun assertContiguousSeq(result: RunResult) {
        assertEquals((0L until result.events.size).toList(), result.events.map { it.seq })
    }

    @Test
    fun `OAI-001 标准 5 个内容 chunk 加 DONE`() {
        val bytes = Sse.bytes(
            Sse.content("你"),
            Sse.content("好"),
            Sse.content("，"),
            Sse.content("世"),
            Sse.content("界", finishReason = "stop"),
            Sse.done(),
        )
        val result = runSession(bytes)
        println("OAI-001 events=${result.lines}")
        assertEquals(listOf("delta", "delta", "delta", "delta", "delta", "done"), result.types)
        assertEquals("你好，世界", result.text)
        assertEquals(FinishReason.STOP, result.done()?.finishReason)
        assertContiguousSeq(result)
        assertEquals(6, result.events.size)
    }

    @Test
    fun `OAI-002 末帧 usage 在 done 之前且只有一条`() {
        val usage = "{\"prompt_tokens\":9,\"completion_tokens\":4,\"total_tokens\":13," +
            "\"prompt_tokens_details\":{\"cached_tokens\":3},\"completion_tokens_details\":{\"reasoning_tokens\":2}}"
        val bytes = Sse.bytes(
            Sse.content("hi"),
            Sse.finishOnly("stop", usage),
            Sse.done(),
        )
        val result = runSession(bytes)
        println("OAI-002 events=${result.lines}")
        assertEquals(listOf("delta", "usage", "done"), result.types)
        val usageEvent = result.usage()!!
        assertEquals(9L, usageEvent.counts.input)
        assertEquals(4L, usageEvent.counts.output)
        assertEquals(3L, usageEvent.counts.cached)
        assertEquals(2L, usageEvent.counts.reasoning)
        assertEquals(13L, usageEvent.counts.total)
        assertEquals(false, usageEvent.counts.estimated)
        assertContiguousSeq(result)
    }

    @Test
    fun `OAI-003 一个 tool_call 的 arguments 分三片且 name 只在首片`() {
        val bytes = Sse.bytes(
            Sse.toolCall(0, "call_a1", "web_search", ""),
            Sse.toolCall(0, null, null, "{\"q\":"),
            Sse.toolCall(0, null, null, "\"天气\"}"),
            Sse.finishOnly("tool_calls"),
            Sse.done(),
        )
        val result = runSession(bytes)
        println("OAI-003 events=${result.lines}")
        val deltas = result.events.filterIsInstance<StreamEvent.ToolCallDelta>()
        assertEquals(3, deltas.size)
        assertEquals("call_a1", deltas[0].id)
        assertEquals("web_search", deltas[0].name)
        assertEquals("", deltas[0].args)
        assertNull(deltas[1].id)
        assertNull(deltas[1].name)
        assertNull(deltas[2].name)
        assertEquals("{\"q\":\"天气\"}", result.toolArgs(0))
        assertEquals(1, result.toolEnds().size)
        assertEquals(FinishReason.TOOL_CALLS, result.done()?.finishReason)
        assertContiguousSeq(result)
    }

    @Test
    fun `OAI-004 两个 tool_call 交错下发各自拼接正确`() {
        val bytes = Sse.bytes(
            Sse.toolCall(0, "call_a", "alpha", "{\"a\":"),
            Sse.toolCall(1, "call_b", "beta", "{\"b\":"),
            Sse.toolCall(0, null, null, "1}"),
            Sse.toolCall(1, null, null, "2}"),
            Sse.finishOnly("tool_calls"),
            Sse.done(),
        )
        val result = runSession(bytes, pattern = ChunkPattern.RandomSlices(7))
        println("OAI-004 events=${result.lines}")
        assertEquals("{\"a\":1}", result.toolArgs(0))
        assertEquals("{\"b\":2}", result.toolArgs(1))
        assertEquals(2, result.toolEnds().size)
        assertEquals(listOf(0, 1), result.toolEnds().map { it.index })
        assertEquals(FinishReason.TOOL_CALLS, result.done()?.finishReason)
        assertContiguousSeq(result)
    }

    @Test
    fun `OAI-005 同帧含 content 与 reasoning_content 时 delta 在前且两通道独立`() {
        val bytes = Sse.bytes(
            Sse.contentAndReasoning("答案", "推理"),
            Sse.finishOnly("stop"),
            Sse.done(),
        )
        val result = runSession(bytes)
        println("OAI-005 events=${result.lines}")
        assertEquals(listOf("delta", "reasoning_delta", "done"), result.types)
        assertEquals("答案", result.text)
        assertEquals("推理", result.reasoning)
        assertTrue(result.reasoning.isNotEmpty() && !result.text.contains("推理"))
    }

    @Test
    fun `OAI-005b reasoning_content 与 reasoning 同帧时前者优先并记 trace`() {
        val frame = "data: {\"choices\":[{\"index\":0,\"delta\":{\"reasoning_content\":\"A\",\"reasoning\":\"B\"}," +
            "\"finish_reason\":null}]}\n\n"
        val result = runSession(Sse.bytes(frame, Sse.finishOnly("stop"), Sse.done()))
        println("OAI-005b events=${result.lines} trace=${result.trace}")
        assertEquals("A", result.reasoning)
        assertEquals(1L, result.trace["reasoning_field_conflict"])
    }

    @Test
    fun `OAI-006 finish_reason length 映射为 done length`() {
        val bytes = Sse.bytes(Sse.content("trunc", finishReason = "length"), Sse.done())
        val result = runSession(bytes)
        println("OAI-006 events=${result.lines}")
        assertEquals(FinishReason.LENGTH, result.done()?.finishReason)
        assertEquals("length", result.done()?.rawFinishReason)
    }

    @Test
    fun `OAI-008 content_filter 且此前有内容时是 done 不是 failed`() {
        val bytes = Sse.bytes(
            Sse.content("partial"),
            Sse.content("content", finishReason = "content_filter"),
            Sse.done(),
        )
        val result = runSession(bytes)
        println("OAI-008 events=${result.lines}")
        assertEquals(FinishReason.CONTENT_FILTER, result.done()?.finishReason)
        assertNull(result.failed())
        assertEquals(2, result.deltas().size)
    }

    @Test
    fun `OAI-009 仅在 duplicate 标记下去重 未标记时两个相同 delta 都要发`() {
        val bytes = Sse.bytes(Sse.content("相同内容"), Sse.content("相同内容"), Sse.finishOnly("stop"), Sse.done())
        val deduped = runSession(bytes, config = StreamConfig(duplicateChunks = true))
        val plain = runSession(bytes)
        println("OAI-009 duplicate=true  events=${deduped.lines}")
        println("OAI-009 duplicate=false events=${plain.lines}")
        assertEquals(1, deduped.deltas().size)
        assertEquals(listOf("delta", "done"), deduped.types)
        assertEquals(2, plain.deltas().size)
        assertEquals("相同内容相同内容", plain.text)
    }

    @Test
    fun `OAI-010 乱序到达仍按到达顺序发事件且 seq 严格加一`() {
        val bytes = Sse.bytes(
            Sse.content("C"),
            Sse.content("A"),
            Sse.content("B", finishReason = "stop"),
            Sse.done(),
        )
        val result = runSession(bytes, pattern = ChunkPattern.Bytes(2))
        println("OAI-010 events=${result.lines}")
        assertEquals("CAB", result.text)
        assertContiguousSeq(result)
        assertEquals(listOf("delta", "delta", "delta", "done"), result.types)
    }

    @Test
    fun `OAI-011 单帧同时含 content 与两个 tool_calls 时按固定顺序产 3 个事件`() {
        val frame = "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"同时\",\"tool_calls\":[" +
            "{\"index\":0,\"id\":\"call_0\",\"type\":\"function\",\"function\":{\"name\":\"f0\",\"arguments\":\"{\"}}," +
            "{\"index\":1,\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"f1\",\"arguments\":\"[\"}}]}," +
            "\"finish_reason\":null}]}\n\n"
        val result = runSession(Sse.bytes(frame, Sse.finishOnly("tool_calls"), Sse.done()))
        println("OAI-011 events=${result.lines}")
        assertEquals(listOf("delta", "tool_call_delta", "tool_call_delta", "tool_call_end", "tool_call_end", "done"),
            result.types)
        assertEquals(listOf(0L, 1L, 2L, 3L, 4L, 5L), result.events.map { it.seq })
        val toolDeltas = result.events.filterIsInstance<StreamEvent.ToolCallDelta>()
        assertEquals(listOf(0, 1), toolDeltas.map { it.index })
        assertEquals("同时", result.text)
    }

    @Test
    fun `OAI-012 累积型端点只发增量并保持最终全文一致 I11`() {
        val config = StreamConfig(cumulativeStream = true)
        val bytes = Sse.bytes(
            Sse.content("He"),
            Sse.content("Hello"),
            Sse.content("Hello world", finishReason = "stop"),
            Sse.done(),
        )
        val result = runSession(bytes, config = config)
        println("OAI-012 events=${result.lines}")
        assertEquals(listOf("He", "llo", " world"), result.deltas().map { it.text })
        assertEquals("Hello world", result.text)
        assertEquals(listOf("delta", "delta", "delta", "done"), result.types)
    }

    @Test
    fun `OAI-012b 非累积端点即使文本重复也原样发射`() {
        val bytes = Sse.bytes(Sse.content("aa"), Sse.content("aa", finishReason = "stop"), Sse.done())
        val result = runSession(bytes)
        println("OAI-012b events=${result.lines}")
        assertEquals(listOf("aa", "aa"), result.deltas().map { it.text })
        assertEquals("aaaa", result.text)
    }

    @Test
    fun `OAI-多 choice 只取 index 0 并给出 notice`() {
        val frame = "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"主\"},\"finish_reason\":null}," +
            "{\"index\":1,\"delta\":{\"content\":\"次\"},\"finish_reason\":null}]}\n\n"
        val result = runSession(Sse.bytes(frame, Sse.finishOnly("stop"), Sse.done()))
        println("多choice events=${result.lines}")
        assertEquals(listOf("delta", "notice", "done"), result.types)
        assertEquals("主", result.text)
        assertEquals(
            NoticeCode.MULTI_CHOICE_IGNORED,
            result.events.filterIsInstance<StreamEvent.Notice>().single().code,
        )
    }
}
