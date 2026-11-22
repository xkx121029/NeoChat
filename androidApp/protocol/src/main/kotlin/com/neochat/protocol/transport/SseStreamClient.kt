package com.neochat.protocol.transport

import com.neochat.protocol.StreamConfig
import com.neochat.protocol.StreamEvent
import com.neochat.protocol.StreamSession
import com.neochat.protocol.errors.ErrorMapper
import com.neochat.protocol.errors.ProviderFacts
import com.neochat.protocol.request.HttpRequestSpec
import java.io.IOException
import java.io.InputStream
import java.net.SocketTimeoutException
import java.util.concurrent.Executors
import java.util.concurrent.ScheduledExecutorService
import java.util.concurrent.ScheduledFuture
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger
import java.util.zip.GZIPInputStream
import java.util.zip.InflaterInputStream
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.Call
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.Response

/**
 * SSE 传输层：OkHttp 读 + L1/L2 管道，负责 2xx/非 2xx 分支、gzip/deflate、首字节与 idle 计时、
 * 以及终止后关闭连接（I12）与取消（I10）。
 */
public class SseStreamClient(
    private val baseClient: OkHttpClient = OkHttpClient.Builder().build(),
) {

    private val active: AtomicInteger = AtomicInteger()
    private val watchdog: ScheduledExecutorService =
        Executors.newSingleThreadScheduledExecutor { runnable ->
            Thread(runnable, "neochat-sse-watchdog").apply { isDaemon = true }
        }

    /** conformance 的 `asserts.socket.stream_count_zero` 钩子。 */
    public fun activeStreamCount(): Int = active.get()

    public fun close() {
        watchdog.shutdownNow()
        baseClient.dispatcher.executorService.shutdown()
        baseClient.connectionPool.evictAll()
    }

    public fun open(
        spec: HttpRequestSpec,
        config: StreamConfig = StreamConfig(),
        timeouts: StreamTimeouts = StreamTimeouts(),
        onEvent: (StreamEvent) -> Unit = {},
    ): StreamHandle {
        val facts = config.facts()
        val session = StreamSession(config, onEvent)
        val call = buildCall(spec, timeouts)
        val state = StreamState().also { it.call = call }
        active.incrementAndGet()
        val watchdogTask = watchdog.scheduleAtFixedRate(
            { guardTimeouts(state, session, timeouts, facts) },
            PERIOD_MS,
            PERIOD_MS,
            TimeUnit.MILLISECONDS,
        )
        val finished = AtomicBoolean(false)
        session.setTerminateListener {
            if (finished.compareAndSet(false, true)) {
                state.closeConnection()
                watchdogTask.cancel(false)
                active.decrementAndGet()
                state.release()
            }
        }
        val reader = Thread({ readStream(call, config, state, session) }, "neochat-sse-reader")
        reader.isDaemon = true
        reader.start()
        return Handle(session, state)
    }

    /** 协程入口：在 IO 线程上等到流终止后返回完整事件序列。 */
    public suspend fun collect(
        spec: HttpRequestSpec,
        config: StreamConfig = StreamConfig(),
        timeouts: StreamTimeouts = StreamTimeouts(),
        timeoutMs: Long = 60_000L,
    ): List<StreamEvent> = withContext(Dispatchers.IO) {
        val handle = open(spec, config, timeouts)
        handle.awaitTermination(timeoutMs)
        handle.events()
    }

    private fun buildCall(spec: HttpRequestSpec, timeouts: StreamTimeouts): Call {
        val client = baseClient.newBuilder()
            .connectTimeout(timeouts.connectMs, TimeUnit.MILLISECONDS)
            .readTimeout(maxOf(timeouts.firstByteMs, timeouts.readIdleMs) + PERIOD_MS, TimeUnit.MILLISECONDS)
            .build()
        val builder = Request.Builder()
            .url(spec.url)
            .post(spec.body.toRequestBody(JSON_MEDIA_TYPE))
        spec.headers.forEach { (key, value) -> builder.header(key, value) }
        return client.newCall(builder.build())
    }

    private fun guardTimeouts(
        state: StreamState,
        session: StreamSession,
        timeouts: StreamTimeouts,
        facts: ProviderFacts,
    ) {
        if (state.isClosed()) return
        val now = System.currentTimeMillis()
        if (!state.firstByteSeen()) {
            if (now - state.startedAt() >= timeouts.firstByteMs) {
                session.failWith(ErrorMapper.firstByteTimeout(facts))
                state.closeConnection()
            }
            return
        }
        if (now - state.lastActivityAt() >= timeouts.readIdleMs) {
            session.failWith(ErrorMapper.readTimeout(facts))
            state.closeConnection()
        }
    }

    private fun readStream(call: Call, config: StreamConfig, state: StreamState, session: StreamSession) {
        val facts = config.facts()
        try {
            call.execute().use { response ->
                state.response = response
                if (!response.isSuccessful || isJsonBody(response)) {
                    val text = response.peekBody(MAX_ERROR_BODY_BYTES).string()
                    session.failWith(ErrorMapper.fromHttp(response.code, headersOf(response), text, facts))
                    return
                }
                state.markFirstByte()
                decodeStream(response).use { body -> pump(body, state, session) }
            }
        } catch (e: SocketTimeoutException) {
            failOnTimeout(state, session, facts)
        } catch (e: IOException) {
            if (!state.cancelRequested) session.failWith(ErrorMapper.connectionReset(facts, e.message))
        } finally {
            state.release()
        }
    }

    private fun failOnTimeout(state: StreamState, session: StreamSession, facts: ProviderFacts) {
        if (state.cancelRequested) return
        val error = if (state.firstByteSeen()) ErrorMapper.readTimeout(facts) else ErrorMapper.firstByteTimeout(facts)
        session.failWith(error)
    }

    private fun pump(stream: InputStream, state: StreamState, session: StreamSession) {
        val buffer = ByteArray(READ_BUFFER_BYTES)
        while (!state.isClosed()) {
            val read = stream.read(buffer)
            if (read < 0) break
            if (read == 0) continue
            state.touch()
            session.feed(buffer, 0, read)
            if (session.isTerminated()) return
        }
        if (!session.isTerminated() && !state.cancelRequested) session.endOfStream()
    }

    private fun decodeStream(response: Response): InputStream {
        val raw = requireNotNull(response.body) { "empty body on 2xx response" }.byteStream()
        val encoding = response.header("Content-Encoding")?.lowercase()
        return when {
            encoding == null -> raw
            encoding.contains("gzip") -> GZIPInputStream(raw)
            encoding.contains("deflate") -> InflaterInputStream(raw)
            else -> raw
        }
    }

    private fun isJsonBody(response: Response): Boolean {
        val contentType = response.header("Content-Type")?.lowercase() ?: return false
        return contentType.contains("application/json")
    }

    private fun headersOf(response: Response): Map<String, String> {
        val out = LinkedHashMap<String, String>()
        for (name in response.headers.names()) out[name] = response.header(name) ?: ""
        return out
    }

    private class Handle(
        private val session: StreamSession,
        private val state: StreamState,
    ) : StreamHandle {

        override fun cancel() {
            state.cancelRequested = true
            state.closeConnection()
            session.abortByUser()
        }

        override fun awaitTermination(timeoutMs: Long): Boolean = state.awaitTermination(timeoutMs)

        override fun isTerminated(): Boolean = session.isTerminated()

        override fun events(): List<StreamEvent> = session.events()

        override fun trace(): Map<String, Long> = session.trace.snapshot()
    }

    private companion object {
        const val PERIOD_MS: Long = 20
        const val READ_BUFFER_BYTES: Int = 8192
        const val MAX_ERROR_BODY_BYTES: Long = 65_536
        val JSON_MEDIA_TYPE = "application/json".toMediaType()
    }
}
