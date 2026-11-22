package com.neochat.protocol

import com.neochat.protocol.errors.ErrorMapper
import com.neochat.protocol.errors.ErrorObject
import com.neochat.protocol.openai.OpenAiChatMapper
import com.neochat.protocol.sse.SseFrame
import com.neochat.protocol.sse.SseFramer
import com.neochat.protocol.sse.SseFrameTooLargeException

/**
 * 一次流式应答的 L1+L2 管道：字节进，StreamEvent 出。
 *
 * 与网络无关，因此可以按任意切包方式驱动（conformance 的 `whole` / `bytes:1` / `random:7`）。
 * 终止事件发出时调用 terminate 监听器，传输层据此关闭 socket（I12）。
 */
public class StreamSession(
    private val config: StreamConfig = StreamConfig(),
    private val onEvent: (StreamEvent) -> Unit = {},
) {

    public val trace: ProtocolTrace = ProtocolTrace()

    private val collected: MutableList<StreamEvent> = ArrayList()
    private var terminateListener: () -> Unit = {}

    private val emitter: StreamEmitter = StreamEmitter(
        trace = trace,
        sink = { event ->
            collected.add(event)
            onEvent(event)
        },
        onTerminate = { terminateListener() },
    )
    private val mapper: OpenAiChatMapper = OpenAiChatMapper(config, emitter, trace)
    private val framer: SseFramer = SseFramer(config.maxFrameBytes, trace) { frame -> dispatch(frame) }

    internal fun setTerminateListener(listener: () -> Unit) {
        terminateListener = listener
    }

    public fun isTerminated(): Boolean = emitter.isTerminated()

    @Synchronized
    public fun events(): List<StreamEvent> = ArrayList(collected)

    @Synchronized
    public fun feed(bytes: ByteArray, offset: Int = 0, length: Int = bytes.size - offset) {
        if (emitter.isTerminated()) return
        try {
            framer.feed(bytes, offset, length)
        } catch (e: SseFrameTooLargeException) {
            emitter.failed(ErrorMapper.parseTooLarge(config.facts(), e.limitBytes))
        }
    }

    /** EOF：有 finish_reason 走宽容路径补发 done，否则 PARSE_INCOMPLETE（stream-events.md §2.3）。 */
    @Synchronized
    public fun endOfStream() {
        if (emitter.isTerminated()) return
        framer.endOfInput()
        mapper.onEndOfStream()
    }

    /** 用户取消：MUST 以 done{aborted_user} 结束，MUST NOT 为 failed（I10）。 */
    @Synchronized
    public fun abortByUser() {
        if (emitter.isTerminated()) return
        emitter.done(FinishReason.ABORTED_USER)
    }

    /** 传输层错误（RST、idle 超时等）。已终止则返回 false，不重复发射。 */
    @Synchronized
    public fun failWith(error: ErrorObject): Boolean {
        if (emitter.isTerminated()) return false
        emitter.failed(error)
        return true
    }

    private fun dispatch(frame: SseFrame) {
        if (emitter.isTerminated()) return
        val payload = frame.data.trim()
        if (payload.isEmpty()) return
        try {
            mapper.onPayload(payload)
        } catch (e: JsonParseException) {
            trace.count("sse_payload_invalid")
            emitter.failed(ErrorMapper.parseInvalid(config.facts(), frame.data))
        }
    }
}
