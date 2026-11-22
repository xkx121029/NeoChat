package com.neochat.protocol.support

import com.neochat.protocol.Json
import com.neochat.protocol.JsonValue
import com.neochat.protocol.ProtocolTrace
import com.neochat.protocol.StreamConfig
import com.neochat.protocol.StreamEvent
import com.neochat.protocol.StreamSession
import com.neochat.protocol.toJsonLines
import java.security.MessageDigest
import java.util.Random

internal sealed interface ChunkPattern {

    data object Whole : ChunkPattern

    data class Bytes(val size: Int) : ChunkPattern

    data class RandomSlices(val seed: Long) : ChunkPattern
}

internal data class RunResult(
    val events: List<StreamEvent>,
    val trace: Map<String, Long>,
) {

    val lines: String get() = events.toJsonLines()

    val types: List<String> get() = events.map { event ->
        when (event) {
            is StreamEvent.Delta -> "delta"
            is StreamEvent.ReasoningDelta -> "reasoning_delta"
            is StreamEvent.ToolCallDelta -> "tool_call_delta"
            is StreamEvent.ToolCallEnd -> "tool_call_end"
            is StreamEvent.Usage -> "usage"
            is StreamEvent.Notice -> "notice"
            is StreamEvent.Done -> "done"
            is StreamEvent.Failed -> "failed"
        }
    }

    val text: String get() = events.filterIsInstance<StreamEvent.Delta>().joinToString("") { it.text }

    val reasoning: String get() = events.filterIsInstance<StreamEvent.ReasoningDelta>().joinToString("") { it.text }

    fun deltas(): List<StreamEvent.Delta> = events.filterIsInstance<StreamEvent.Delta>()

    fun toolArgs(index: Int): String =
        events.filterIsInstance<StreamEvent.ToolCallDelta>().filter { it.index == index }
            .joinToString("") { it.args ?: "" }

    fun toolEnds(): List<StreamEvent.ToolCallEnd> = events.filterIsInstance<StreamEvent.ToolCallEnd>()

    fun usage(): StreamEvent.Usage? = events.filterIsInstance<StreamEvent.Usage>().firstOrNull()

    fun done(): StreamEvent.Done? = events.filterIsInstance<StreamEvent.Done>().firstOrNull()

    fun failed(): StreamEvent.Failed? = events.filterIsInstance<StreamEvent.Failed>().firstOrNull()
}

internal fun slice(bytes: ByteArray, pattern: ChunkPattern): List<ByteArray> = when (pattern) {
    ChunkPattern.Whole -> listOf(bytes)
    is ChunkPattern.Bytes -> generateSequence(0) { it + pattern.size }
        .takeWhile { it < bytes.size }
        .map { offset -> bytes.copyOfRange(offset, minOf(offset + pattern.size, bytes.size)) }
        .toList()
    is ChunkPattern.RandomSlices -> {
        val random = Random(pattern.seed)
        val out = ArrayList<ByteArray>()
        var offset = 0
        while (offset < bytes.size) {
            val size = 1 + random.nextInt(64)
            out.add(bytes.copyOfRange(offset, minOf(offset + size, bytes.size)))
            offset += size
        }
        out
    }
}

internal fun runSession(
    bytes: ByteArray,
    config: StreamConfig = StreamConfig(),
    pattern: ChunkPattern = ChunkPattern.Whole,
): RunResult {
    val session = StreamSession(config)
    slice(bytes, pattern).forEach { chunk -> session.feed(chunk) }
    session.endOfStream()
    return RunResult(session.events(), session.trace.snapshot())
}

internal fun sha256(text: String): String {
    val digest = MessageDigest.getInstance("SHA-256").digest(text.toByteArray(Charsets.UTF_8))
    return digest.joinToString("") { byte -> "%02x".format(byte) }
}

internal fun quote(text: String): String = Json.write(JsonValue.Str(text))

internal object Sse {

    fun frame(json: String): String = "data: $json\n\n"

    fun done(): String = "data: [DONE]\n\n"

    fun comment(text: String): String = ": $text\n\n"

    fun content(text: String, finishReason: String? = null): String {
        val finish = finishReason?.let { ",\"finish_reason\":${quote(it)}" } ?: ",\"finish_reason\":null"
        return frame("{\"id\":\"c1\",\"choices\":[{\"index\":0,\"delta\":{\"content\":${quote(text)}}$finish}]}")
    }

    fun reasoning(text: String, field: String = "reasoning_content"): String =
        frame("{\"choices\":[{\"index\":0,\"delta\":{\"$field\":${quote(text)}},\"finish_reason\":null}]}")

    fun contentAndReasoning(content: String, reasoning: String): String = frame(
        "{\"choices\":[{\"index\":0,\"delta\":{\"content\":${quote(content)}," +
            "\"reasoning_content\":${quote(reasoning)}},\"finish_reason\":null}]}",
    )

    fun toolCall(index: Int, id: String?, name: String?, args: String?): String {
        val parts = StringBuilder()
        parts.append("{\"index\":$index")
        if (id != null) parts.append(",\"id\":${quote(id)},\"type\":\"function\"")
        val fn = StringBuilder()
        if (name != null) fn.append("\"name\":${quote(name)}")
        if (args != null) {
            if (fn.isNotEmpty()) fn.append(',')
            fn.append("\"arguments\":${quote(args)}")
        }
        if (fn.isNotEmpty()) {
            parts.append(",\"function\":{")
            parts.append(fn)
            parts.append('}')
        }
        parts.append('}')
        return frame("{\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[$parts]},\"finish_reason\":null}]}")
    }

    fun usage(prompt: Int, completion: Int, total: Int? = null, cached: Int? = null): String {
        val details = cached?.let { ",\"prompt_tokens_details\":{\"cached_tokens\":$it}" } ?: ""
        val totalField = total?.let { ",\"total_tokens\":$it" } ?: ""
        return frame(
            "{\"choices\":[],\"usage\":{\"prompt_tokens\":$prompt,\"completion_tokens\":$completion$totalField$details}}",
        )
    }

    fun finishOnly(reason: String, usageJson: String? = null): String {
        val usage = usageJson?.let { ",\"usage\":$it" } ?: ""
        return frame("{\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":${quote(reason)}}]$usage}")
    }

    fun bytes(vararg parts: String): ByteArray = parts.joinToString("").toByteArray(Charsets.UTF_8)
}

internal fun List<StreamEvent>.ended(): Boolean =
    lastOrNull()?.let { it is StreamEvent.Done || it is StreamEvent.Failed } == true

internal fun waitUntil(timeoutMs: Long, condition: () -> Boolean): Boolean {
    val deadline = System.currentTimeMillis() + timeoutMs
    while (System.currentTimeMillis() < deadline) {
        if (condition()) return true
        Thread.sleep(10)
    }
    return condition()
}
