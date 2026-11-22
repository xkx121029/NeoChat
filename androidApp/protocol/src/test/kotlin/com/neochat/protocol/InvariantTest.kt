package com.neochat.protocol

import com.neochat.protocol.errors.ErrorCatalog
import com.neochat.protocol.errors.ErrorCode
import com.neochat.protocol.errors.ProviderFacts
import com.neochat.protocol.support.Sse
import com.neochat.protocol.support.runSession
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/** 逐条不变量 I1..I12 的专门断言（与 conformance 用例互补）。 */
class InvariantTest {

    private val richStream = Sse.bytes(
        Sse.content("A"),
        Sse.reasoning("R1"),
        Sse.toolCall(0, "call_0", "f0", ""),
        Sse.toolCall(0, null, null, "{\"a\":1}"),
        Sse.toolCall(1, "call_1", "f1", "{}"),
        Sse.content("B", finishReason = "tool_calls"),
        Sse.usage(11, 22),
        Sse.done(),
    )

    @Test
    fun `I1 seq 从 0 开始严格加一无重复无空洞`() {
        val result = runSession(richStream)
        println("I1 seqs=${result.events.map { it.seq }}")
        assertEquals((0L until result.events.size).toList(), result.events.map { it.seq })
    }

    @Test
    fun `I2 I3 终止事件唯一且在末尾`() {
        val result = runSession(richStream)
        val terminals = result.events.filter { it is StreamEvent.Done || it is StreamEvent.Failed }
        assertEquals(1, terminals.size)
        assertTrue(result.events.last() === terminals.single())
    }

    @Test
    fun `I4 usage 至多一条且在终止事件之前`() {
        val result = runSession(richStream)
        val usages = result.events.filterIsInstance<StreamEvent.Usage>()
        assertEquals(1, usages.size)
        assertTrue(result.events.indexOf(usages.single()) < result.events.size - 1)
    }

    @Test
    fun `I5 每个打开过的 index 恰好一个 tool_call_end 且都在 done 之前`() {
        val result = runSession(richStream)
        val ends = result.toolEnds().map { it.index }
        assertEquals(listOf(0, 1), ends)
        val doneIndex = result.events.indexOfFirst { it is StreamEvent.Done }
        result.toolEnds().forEach { end -> assertTrue(result.events.indexOf(end) < doneIndex) }
    }

    @Test
    fun `I6 空 delta 不发射 专门的断言`() {
        val bytes = Sse.bytes(
            Sse.content(""),
            Sse.reasoning(""),
            Sse.content(""),
            Sse.content("only", finishReason = "stop"),
            Sse.done(),
        )
        val result = runSession(bytes)
        println("I6 events=${result.lines}")
        assertEquals(1, result.deltas().size)
        assertTrue(result.deltas().all { it.text.isNotEmpty() })
        assertTrue(result.events.filterIsInstance<StreamEvent.ReasoningDelta>().isEmpty())
        assertEquals("only", result.text)
    }

    @Test
    fun `I7 首片必须带 id 与 name 后续不得重复 name`() {
        val result = runSession(richStream)
        val deltas = result.events.filterIsInstance<StreamEvent.ToolCallDelta>()
        val first0 = deltas.first { it.index == 0 }
        assertEquals("call_0", first0.id)
        assertEquals("f0", first0.name)
        assertEquals("", first0.args)
        deltas.filter { it.index == 0 }.drop(1).forEach { later ->
            assertNull(later.id)
            assertNull(later.name)
        }
    }

    @Test
    fun `I7b id 缺失时合成 call 下划线 index`() {
        val frame = "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[" +
            "{\"index\":3,\"function\":{\"name\":\"anon\",\"arguments\":\"{}\"}}]},\"finish_reason\":null}]}\n\n"
        val result = runSession(Sse.bytes(frame, Sse.finishOnly("tool_calls"), Sse.done()))
        val first = result.events.filterIsInstance<StreamEvent.ToolCallDelta>().first()
        println("I7b first=${first.toJsonLine()}")
        assertEquals("call_3", first.id)
        assertEquals("anon", first.name)
    }

    @Test
    fun `I8 一帧多事件顺序固定且事件不跨帧拆分`() {
        val frame = "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"X\",\"tool_calls\":[" +
            "{\"index\":0,\"id\":\"c0\",\"function\":{\"name\":\"f0\",\"arguments\":\"a\"}}," +
            "{\"index\":1,\"id\":\"c1\",\"function\":{\"name\":\"f1\",\"arguments\":\"b\"}}]},\"finish_reason\":null}]}\n\n"
        val whole = runSession(Sse.bytes(frame, Sse.finishOnly("tool_calls"), Sse.done()))
        val bytewise = runSession(
            Sse.bytes(frame, Sse.finishOnly("tool_calls"), Sse.done()),
            pattern = com.neochat.protocol.support.ChunkPattern.Bytes(1),
        )
        println("I8 events=${whole.types}")
        assertEquals(listOf("delta", "tool_call_delta", "tool_call_delta", "tool_call_end", "tool_call_end", "done"),
            whole.types)
        assertEquals(listOf(0, 1), whole.events.filterIsInstance<StreamEvent.ToolCallDelta>().map { it.index })
        assertEquals(whole.lines, bytewise.lines)
    }

    @Test
    fun `I9 心跳与空帧不产生事件`() {
        val bytes = Sse.bytes(": ping\n\n", "\n\n", ": OPENROUTER PROCESSING\n\n", Sse.content("x"), Sse.done())
        val result = runSession(bytes)
        println("I9 events=${result.lines}")
        assertEquals(listOf("delta", "done"), result.types)
    }

    @Test
    fun `I10 用户取消是 done aborted_user 不是 failed 专门的断言`() {
        val session = StreamSession()
        session.feed(Sse.content("部分").toByteArray(Charsets.UTF_8))
        session.abortByUser()
        session.feed(Sse.content("不该出现").toByteArray(Charsets.UTF_8))
        session.endOfStream()
        val events = session.events()
        println("I10 events=${events.toJsonLines()}")
        assertEquals(listOf("delta", "done"), events.map { if (it is StreamEvent.Done) "done" else "delta" })
        assertEquals(FinishReason.ABORTED_USER, (events.last() as StreamEvent.Done).finishReason)
        assertTrue(events.none { it is StreamEvent.Failed })
        assertEquals("部分", events.filterIsInstance<StreamEvent.Delta>().joinToString("") { it.text })
    }

    @Test
    fun `I11 累积端点不重发已发前缀 专门的断言`() {
        val config = StreamConfig(cumulativeStream = true)
        val bytes = Sse.bytes(
            Sse.content("一二"),
            Sse.content("一二三"),
            Sse.content("一二三四", finishReason = "stop"),
            Sse.done(),
        )
        val result = runSession(bytes, config = config)
        println("I11 events=${result.lines}")
        assertEquals(listOf("一二", "三", "四"), result.deltas().map { it.text })
        assertEquals("一二三四", result.text)
        assertEquals(3, result.deltas().size)
    }

    @Test
    fun `I11b 累积端点重写非前缀文本时按整段补发并记 trace`() {
        val config = StreamConfig(cumulativeStream = true)
        val bytes = Sse.bytes(Sse.content("abc"), Sse.content("xyz", finishReason = "stop"), Sse.done())
        val result = runSession(bytes, config = config)
        println("I11b events=${result.lines} trace=${result.trace}")
        assertEquals(listOf("abc", "xyz"), result.deltas().map { it.text })
        assertEquals(1L, result.trace["cumulative_rewrite"])
    }

    @Test
    fun `PARSE_SSE_INVALID 非 JSON 载荷报错且不带出半条事件`() {
        val bytes = Sse.bytes("data: this-is-not-json\n\n", Sse.done())
        val result = runSession(bytes)
        println("PARSE events=${result.lines}")
        assertEquals(ErrorCode.PARSE_SSE_INVALID, result.failed()?.error?.code)
    }

    @Test
    fun `HTTP 200 的 SSE 内错误帧映射为 failed 并终止`() {
        val bytes = Sse.bytes(
            Sse.content("半"),
            Sse.frame("{\"error\":{\"message\":\"insufficient quota\",\"code\":\"insufficient_quota\"}}"),
            Sse.content("不该出现"),
        )
        val result = runSession(bytes)
        println("STREAM-ERR events=${result.lines}")
        assertEquals(ErrorCode.AUTH_INSUFFICIENT_QUOTA, result.failed()?.error?.code)
        assertEquals("半", result.text)
        assertFalse(result.events.any { it is StreamEvent.Done })
    }

    @Test
    fun `错误码目录齐备 causes 一到三条 actions 全在闭集内`() {
        val allowed = setOf(
            "retry", "retry_after", "edit_provider", "open_provider_settings", "manage_keys", "check_proxy",
            "check_base_url", "switch_model", "reduce_context", "top_up", "open_docs", "view_raw",
            "copy_diagnostics", "retry_tool", "db_check", "open_network_settings", "refresh_models",
            "clean_storage", "view_capability_matrix", "view_service_status", "open_mcp_settings",
        )
        val facts = ProviderFacts(provider = "openai_compatible", model = "m", billingUrl = "b", statusUrl = "s")
        val bare = ProviderFacts(provider = "openai_compatible", model = "m")
        ErrorCode.entries.forEach { code ->
            val definition = ErrorCatalog.define(code, facts)
            println(
                "CATALOG ${code.wire} stage=${definition.stage.wire} " +
                    "retryable=${definition.retryable} causes=${definition.causes.size} " +
                    "actions=${definition.actions.map { it.a }}",
            )
            assertTrue(definition.causes.size in 1..3, "$code causes 数量越界")
            assertTrue(definition.actions.isNotEmpty(), "$code actions 为空")
            assertTrue(definition.actions.all { it.a in allowed }, "$code 含闭集外 action")
            assertTrue(definition.causes.all { it.startsWith("errors.${code.wire}.cause.") })
        }
        assertTrue(ErrorCatalog.define(ErrorCode.AUTH_INSUFFICIENT_QUOTA, bare).actions.none { it.a == "top_up" })
        assertTrue(ErrorCatalog.define(ErrorCode.SERVER_ERROR, bare).actions.none { it.a == "view_service_status" })
    }
}
