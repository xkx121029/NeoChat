package com.neochat.protocol

import com.neochat.protocol.errors.ErrorCode
import com.neochat.protocol.request.HttpRequestSpec
import com.neochat.protocol.support.RawHttpServer
import com.neochat.protocol.support.Sse
import com.neochat.protocol.support.ended
import com.neochat.protocol.support.waitUntil
import com.neochat.protocol.transport.SseStreamClient
import com.neochat.protocol.transport.StreamTimeouts
import java.io.ByteArrayOutputStream
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger
import java.util.zip.GZIPOutputStream
import kotlinx.coroutines.runBlocking
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/** HTTP/传输层用例：FRAME-012、FRAME-013、OAI-007、ERR-001 ~ ERR-006。 */
class TransportConformanceTest {

    private val clients = ArrayList<SseStreamClient>()
    private val servers = ArrayList<RawHttpServer>()

    private fun client(): SseStreamClient = SseStreamClient().also { clients.add(it) }

    private fun server(handler: (com.neochat.protocol.support.RawRequest, com.neochat.protocol.support.RawSink) -> Unit) =
        RawHttpServer(handler).also { servers.add(it) }

    private fun spec(url: String): HttpRequestSpec = HttpRequestSpec(
        url = url,
        headers = mapOf("Content-Type" to "application/json", "Accept" to "text/event-stream"),
        body = "{\"model\":\"test\",\"stream\":true}".toByteArray(Charsets.UTF_8),
    )

    private fun collectEvents(
        url: String,
        config: StreamConfig = StreamConfig(),
        timeouts: StreamTimeouts = StreamTimeouts(),
        timeoutMs: Long = 8000,
    ): List<StreamEvent> = runBlocking { client().collect(spec(url), config, timeouts, timeoutMs) }

    private fun gzip(bytes: ByteArray): ByteArray {
        val out = ByteArrayOutputStream()
        GZIPOutputStream(out).use { it.write(bytes) }
        return out.toByteArray()
    }

    @AfterEach
    fun tearDown() {
        clients.forEach { it.close() }
        servers.forEach { it.close() }
    }

    @Test
    fun `FRAME-012 done 之后连接在 100ms 内归零 I12`() {
        val serverClosed = CountDownLatch(1)
        val server = server { _, sink ->
            sink.eventStream()
            sink.chunk(Sse.content("hello", finishReason = "stop"))
            sink.chunk(Sse.done())
            if (sink.awaitClientClose(3000)) serverClosed.countDown()
        }
        val client = client()
        val handle = client.open(spec(server.url), timeouts = StreamTimeouts(readIdleMs = 5000))
        assertTrue(handle.awaitTermination(5000), "流未在 5s 内终止")
        val zeroAt = System.currentTimeMillis()
        val zero = waitUntil(100) { client.activeStreamCount() == 0 }
        val elapsed = System.currentTimeMillis() - zeroAt
        println("FRAME-012 events=${handle.events().map { it.toJsonLine() }}")
        println("FRAME-012 activeStreamCount=${client.activeStreamCount()} elapsedMs=$elapsed")
        assertTrue(handle.events().ended())
        assertTrue(zero, "done 之后 activeStreamCount 未在 100ms 内归零")
        assertTrue(serverClosed.await(3, TimeUnit.SECONDS), "服务端未观察到连接关闭")
        assertTrue(elapsed < 100, "归零耗时 ${elapsed}ms 超过 100ms")
    }

    @Test
    fun `FRAME-013 gzip 压缩流解压后事件序列与非压缩逐字节相同`() {
        val plain = Sse.bytes(Sse.content("压缩"), Sse.content("正文", finishReason = "stop"), Sse.done())
        val plainServer = server { _, sink ->
            sink.fixedBody(200, "text/event-stream", plain)
        }
        val gzipServer = server { _, sink ->
            sink.fixedBody(
                200,
                "text/event-stream",
                gzip(plain),
                mapOf("Content-Encoding" to "gzip"),
            )
        }
        val plainEvents = collectEvents(plainServer.url, timeoutMs = 5000)
        val gzipEvents = collectEvents(gzipServer.url, timeoutMs = 5000)
        println("FRAME-013 plain = ${plainEvents.toJsonLines().trim()}")
        println("FRAME-013 gzip  = ${gzipEvents.toJsonLines().trim()}")
        assertEquals(plainEvents.toJsonLines(), gzipEvents.toJsonLines())
        assertEquals("压缩正文", plainEvents.filterIsInstance<StreamEvent.Delta>().joinToString("") { it.text })
    }

    @Test
    fun `FRAME-013b 截断的 gzip 流报 NET_CONNECTION_RESET`() {
        val plain = Sse.bytes(Sse.content("压缩正文", finishReason = "stop"), Sse.done())
        val truncated = gzip(plain).copyOfRange(0, 20)
        val server = server { _, sink -> sink.fixedBody(200, "text/event-stream", truncated, mapOf("Content-Encoding" to "gzip")) }
        val events = collectEvents(server.url, timeoutMs = 5000)
        println("FRAME-013b events=${events.toJsonLines()}")
        val failed = events.filterIsInstance<StreamEvent.Failed>().singleOrNull()
        assertNotNull(failed)
        assertEquals(ErrorCode.NET_CONNECTION_RESET, failed?.error?.code)
    }

    @Test
    fun `OAI-007 HTTP 200 但 body 是 JSON 错误对象时映射为 AUTH_INSUFFICIENT_QUOTA`() {
        val server = server { _, sink ->
            sink.jsonError(200, "{\"error\":{\"message\":\"quota\",\"code\":\"insufficient_quota\"}}")
        }
        val events = collectEvents(
            server.url,
            config = StreamConfig(provider = "openai_compatible", model = "deepseek-chat", billingUrl = "https://billing.example"),
            timeoutMs = 5000,
        )
        val failed = events.filterIsInstance<StreamEvent.Failed>().single()
        println("OAI-007 events=${events.toJsonLines()}")
        assertEquals(ErrorCode.AUTH_INSUFFICIENT_QUOTA, failed.error.code)
        assertTrue(failed.error.raw!!.toByteArray(Charsets.UTF_8).size <= 512)
        assertTrue(failed.error.actions.any { it.a == "top_up" })
    }

    @Test
    fun `ERR-001 HTTP 401 映射为 AUTH_INVALID_KEY 且含 edit_provider`() {
        val server = server { _, sink -> sink.jsonError(401, "{\"error\":{\"code\":\"invalid_api_key\"}}") }
        val events = collectEvents(
            server.url,
            config = StreamConfig(provider = "openai_compatible", model = "gpt-x"),
            timeoutMs = 5000,
        )
        val error = events.filterIsInstance<StreamEvent.Failed>().single().error
        println("ERR-001 events=${events.toJsonLines()}")
        assertEquals(ErrorCode.AUTH_INVALID_KEY, error.code)
        assertFalse(error.retryable)
        assertTrue(error.actions.any { it.a == "edit_provider" })
        assertEquals(401, error.httpStatus)
    }

    @Test
    fun `ERR-002 HTTP 429 insufficient_quota 映射为 AUTH_INSUFFICIENT_QUOTA 且含 top_up`() {
        val server = server { _, sink ->
            sink.jsonError(429, "{\"error\":{\"code\":\"insufficient_quota\",\"message\":\"You exceeded your current quota\"}}")
        }
        val events = collectEvents(
            server.url,
            config = StreamConfig(billingUrl = "https://billing.example"),
            timeoutMs = 5000,
        )
        val error = events.filterIsInstance<StreamEvent.Failed>().single().error
        println("ERR-002 events=${events.toJsonLines()}")
        assertEquals(ErrorCode.AUTH_INSUFFICIENT_QUOTA, error.code)
        assertTrue(error.actions.any { it.a == "top_up" })
    }

    @Test
    fun `ERR-002b HTTP 429 其他原因映射为 RATE_LIMITED 并带 retry_after_ms`() {
        val server = server { _, sink ->
            sink.jsonError(
                429,
                "{\"error\":{\"message\":\"Rate limit reached\",\"code\":\"rate_limit_exceeded\"}}",
                mapOf("Retry-After" to "20"),
            )
        }
        val events = collectEvents(server.url, timeoutMs = 5000)
        val error = events.filterIsInstance<StreamEvent.Failed>().single().error
        println("ERR-002b events=${events.toJsonLines()}")
        assertEquals(ErrorCode.RATE_LIMITED, error.code)
        assertEquals(20_000L, error.retryAfterMs)
        assertTrue(error.actions.any { it.a == "retry_after" })
    }

    @Test
    fun `ERR-003 HTTP 400 context_length_exceeded 映射为 CONTEXT_OVERFLOW 且含 reduce_context`() {
        val server = server { _, sink ->
            sink.jsonError(
                400,
                "{\"error\":{\"message\":\"This model's maximum context length is 8192 tokens\"," +
                    "\"code\":\"context_length_exceeded\"}}",
            )
        }
        val events = collectEvents(server.url, timeoutMs = 5000)
        val error = events.filterIsInstance<StreamEvent.Failed>().single().error
        println("ERR-003 events=${events.toJsonLines()}")
        assertEquals(ErrorCode.CONTEXT_OVERFLOW, error.code)
        assertTrue(error.actions.any { it.a == "reduce_context" })
    }

    @Test
    fun `ERR-004 HTTP 404 且 message 含 model 映射为 MODEL_NOT_FOUND`() {
        val server = server { _, sink ->
            sink.jsonError(404, "{\"error\":{\"message\":\"The model gpt-x does not exist\",\"code\":\"model_not_found\"}}")
        }
        val events = collectEvents(server.url, timeoutMs = 5000)
        val error = events.filterIsInstance<StreamEvent.Failed>().single().error
        println("ERR-004 events=${events.toJsonLines()}")
        assertEquals(ErrorCode.MODEL_NOT_FOUND, error.code)
    }

    @Test
    fun `ERR-005 传输中途 RST 报 NET_CONNECTION_RESET 且保留已生成内容`() {
        val server = server { _, sink ->
            sink.eventStream()
            sink.chunk(Sse.content("已生成"))
            sink.chunk(Sse.content("内容"))
            Thread.sleep(200)
            sink.resetConnection()
        }
        val events = collectEvents(server.url, timeoutMs = 5000)
        println("ERR-005 events=${events.toJsonLines()}")
        assertEquals("已生成内容", events.filterIsInstance<StreamEvent.Delta>().joinToString("") { it.text })
        val error = events.filterIsInstance<StreamEvent.Failed>().single().error
        assertEquals(ErrorCode.NET_CONNECTION_RESET, error.code)
        assertTrue(events.last() is StreamEvent.Failed)
    }

    @Test
    fun `ERR-006 长时间无字节报 NET_READ_TIMEOUT`() {
        val server = server { _, sink ->
            sink.eventStream()
            sink.chunk(": keep-alive\n\n")
            Thread.sleep(3000)
            sink.end()
        }
        val started = System.currentTimeMillis()
        val events = collectEvents(
            server.url,
            timeouts = StreamTimeouts(readIdleMs = 500),
            timeoutMs = 5000,
        )
        val elapsed = System.currentTimeMillis() - started
        println("ERR-006 events=${events.toJsonLines()} elapsedMs=$elapsed")
        val error = events.filterIsInstance<StreamEvent.Failed>().single().error
        assertEquals(ErrorCode.NET_READ_TIMEOUT, error.code)
        assertTrue(elapsed < 3000)
    }

    @Test
    fun `FRAME-003b 心跳字节重置 read_idle 不误判超时`() {
        val closedEarly = AtomicBoolean(false)
        val server = server { _, sink ->
            sink.eventStream()
            repeat(6) {
                sink.chunk(": keep-alive\n\n")
                Thread.sleep(120)
            }
            sink.chunk(Sse.content("ok", finishReason = "stop"))
            sink.chunk(Sse.done())
            if (!sink.awaitClientClose(2000)) closedEarly.set(true)
        }
        val events = collectEvents(
            server.url,
            timeouts = StreamTimeouts(readIdleMs = 300),
            timeoutMs = 8000,
        )
        println("FRAME-003b events=${events.toJsonLines()}")
        assertTrue(events.none { it is StreamEvent.Failed }, "心跳期间不应报读超时")
        assertEquals("ok", events.filterIsInstance<StreamEvent.Delta>().joinToString("") { it.text })
    }

    @Test
    fun `取消关闭底层连接并补 done aborted_user`() {
        val totalChunks = 200
        val written = AtomicInteger()
        val connectionClosed = CountDownLatch(1)
        val server = server { _, sink ->
            sink.eventStream()
            try {
                repeat(totalChunks) { index ->
                    sink.chunk(Sse.content("c$index"))
                    written.incrementAndGet()
                    Thread.sleep(30)
                }
                sink.end()
            } catch (e: Exception) {
                connectionClosed.countDown()
            } finally {
                sink.close()
            }
        }
        val client = client()
        val handle = client.open(spec(server.url), timeouts = StreamTimeouts(readIdleMs = 5000))
        val gotDeltas = waitUntil(5000) { handle.events().count { it is StreamEvent.Delta } >= 3 }
        assertTrue(gotDeltas, "取消前未收到足够增量")
        handle.cancel()
        assertTrue(handle.awaitTermination(3000), "取消后流未终止")
        val events = handle.events()
        println("取消 events=${events.toJsonLines()} serverClosed=${connectionClosed.await(3, TimeUnit.SECONDS)} " +
            "writtenChunks=${written.get()}/$totalChunks activeStreams=${client.activeStreamCount()}")
        assertEquals(1, events.count { it is StreamEvent.Done })
        val done = events.last() as StreamEvent.Done
        assertEquals(FinishReason.ABORTED_USER, done.finishReason)
        assertTrue(events.none { it is StreamEvent.Failed }, "取消不得产生 failed（I10）")
        assertTrue(connectionClosed.await(3, TimeUnit.SECONDS), "服务端未观察到 socket 关闭")
        assertTrue(written.get() < totalChunks, "服务端在取消后仍能继续写入，说明连接未关闭")
        assertTrue(waitUntil(1000) { client.activeStreamCount() == 0 })
    }
}
