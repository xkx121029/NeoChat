package com.neochat.protocol.sse

import com.neochat.protocol.ProtocolTrace
import com.neochat.protocol.utf8ByteLength
import java.nio.ByteBuffer
import java.nio.CharBuffer
import java.nio.charset.CharsetDecoder
import java.nio.charset.CoderResult
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets

/** 一个 SSE 帧（空行分隔后的完整事件）。 */
public data class SseFrame(
    public val data: String,
    public val event: String?,
    public val id: String?,
    public val retry: Long?,
    public val payloadBytes: Long,
)

/** 单帧/单行载荷超过上限（stream-events.md §2.1.10）。 */
public class SseFrameTooLargeException(public val limitBytes: Long) :
    Exception("SSE frame exceeds $limitBytes bytes")

/**
 * L1 分帧层（stream-events.md §2.1）：
 * 有状态 UTF-8 解码、`\n`/`\r\n`/`\r`、多行 data 拼接、注释心跳、BOM 丢弃、非法字节替换、16 MiB 上限。
 * 输入是原始字节，输出是 [SseFrame]；不做任何事件语义解释。
 */
public class SseFramer(
    public val maxFrameBytes: Long = DEFAULT_MAX_FRAME_BYTES,
    private val trace: ProtocolTrace = ProtocolTrace(),
    private val onFrame: (SseFrame) -> Unit,
) {

    public companion object {
        public const val DEFAULT_MAX_FRAME_BYTES: Long = 16L * 1024 * 1024
        public const val MIN_MAX_FRAME_BYTES: Long = 1024L * 1024
        private const val DECODE_BUFFER_CHARS: Int = 8192
        private val EMPTY: ByteArray = ByteArray(0)
    }

    init {
        require(maxFrameBytes >= MIN_MAX_FRAME_BYTES) {
            "maxFrameBytes=$maxFrameBytes 低于下限 $MIN_MAX_FRAME_BYTES"
        }
    }

    private val decoder: CharsetDecoder = StandardCharsets.UTF_8.newDecoder()
        .onMalformedInput(CodingErrorAction.REPORT)
        .onUnmappableCharacter(CodingErrorAction.REPORT)

    private var pending: ByteArray = EMPTY
    private val line: StringBuilder = StringBuilder()
    private var lineBytes: Long = 0
    private val data: StringBuilder = StringBuilder()
    private var hasData: Boolean = false
    private var payloadBytes: Long = 0
    private var eventName: String? = null
    private var frameId: String? = null
    private var retry: Long? = null
    private var sawField: Boolean = false
    private var lastWasCr: Boolean = false
    private var firstChar: Boolean = true

    public fun feed(bytes: ByteArray, offset: Int = 0, length: Int = bytes.size - offset) {
        val buffer = mergePending(bytes, offset, length)
        val chars = CharBuffer.allocate(DECODE_BUFFER_CHARS)
        while (true) {
            val result = decoder.decode(buffer, chars, false)
            chars.flip()
            if (chars.hasRemaining()) consume(chars.toString())
            chars.clear()
            when {
                result.isUnderflow -> break
                result.isOverflow -> continue
                else -> skipInvalidBytes(buffer, result)
            }
        }
        if (buffer.hasRemaining()) {
            pending = ByteArray(buffer.remaining()).also { buffer.get(it) }
        }
    }

    /**
     * 输入结束。未以空行收尾的残帧按 SSE 规范丢弃：半条 JSON 不得产生事件（stream-events.md I8）。
     */
    public fun endOfInput() {
        pending = EMPTY
        line.setLength(0)
        lineBytes = 0
        resetFrame()
    }

    private fun mergePending(bytes: ByteArray, offset: Int, length: Int): ByteBuffer {
        if (pending.isEmpty()) {
            return ByteBuffer.wrap(bytes, offset, length)
        }
        val merged = ByteArray(pending.size + length)
        System.arraycopy(pending, 0, merged, 0, pending.size)
        System.arraycopy(bytes, offset, merged, pending.size, length)
        pending = EMPTY
        return ByteBuffer.wrap(merged)
    }

    private fun skipInvalidBytes(buffer: ByteBuffer, result: CoderResult) {
        val skipped = result.length()
        val replacement = if (result.isMalformed) "\uFFFD" else "\uFFFD"
        consume(replacement)
        buffer.position(buffer.position() + skipped)
        trace.count("invalid_utf8_bytes", skipped.toLong())
        decoder.reset()
    }

    private fun consume(chars: CharSequence) {
        for (ch in chars) {
            if (firstChar) {
                firstChar = false
                if (ch == '\uFEFF') {
                    trace.count("bom_stripped")
                    continue
                }
            }
            when {
                ch == '\r' -> {
                    finishLine()
                    lastWasCr = true
                }
                ch == '\n' -> {
                    if (!lastWasCr) finishLine()
                    lastWasCr = false
                }
                else -> {
                    lastWasCr = false
                    appendChar(ch)
                }
            }
        }
    }

    private fun appendChar(ch: Char) {
        line.append(ch)
        lineBytes += utf8Cost(ch)
        if (lineBytes > maxFrameBytes) throw SseFrameTooLargeException(maxFrameBytes)
    }

    private fun utf8Cost(ch: Char): Long = when {
        ch.isHighSurrogate() -> 4L
        ch.isLowSurrogate() -> 0L
        ch.code < 0x80 -> 1L
        ch.code < 0x800 -> 2L
        else -> 3L
    }

    private fun finishLine() {
        if (line.isEmpty()) {
            dispatch()
            return
        }
        if (line[0] == ':') {
            trace.count("comments_skipped")
        } else {
            parseField(line.toString())
        }
        line.setLength(0)
        lineBytes = 0
    }

    private fun parseField(text: String) {
        val colon = text.indexOf(':')
        val name = if (colon < 0) text else text.substring(0, colon)
        val rawValue = if (colon < 0) "" else text.substring(colon + 1)
        val value = if (rawValue.startsWith(" ")) rawValue.substring(1) else rawValue
        sawField = true
        when (name) {
            "data" -> appendData(value)
            "event" -> eventName = value
            "id" -> frameId = value
            "retry" -> retry = value.trim().toLongOrNull()
            else -> trace.count("sse_field_ignored")
        }
    }

    private fun appendData(value: String) {
        if (hasData) data.append('\n')
        data.append(value)
        hasData = true
        payloadBytes += utf8ByteLength(value)
        if (payloadBytes > maxFrameBytes) throw SseFrameTooLargeException(maxFrameBytes)
    }

    private fun dispatch() {
        if (!sawField) {
            resetFrame()
            return
        }
        val frame = SseFrame(data.toString(), eventName, frameId, retry, payloadBytes)
        resetFrame()
        onFrame(frame)
    }

    private fun resetFrame() {
        data.setLength(0)
        hasData = false
        payloadBytes = 0
        eventName = null
        frameId = null
        retry = null
        sawField = false
    }
}
