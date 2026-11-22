package com.neochat.protocol

import com.neochat.protocol.support.ChunkPattern
import com.neochat.protocol.support.Sse
import com.neochat.protocol.support.runSession
import com.neochat.protocol.support.sha256
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test

/**
 * 验收第 2 条：同一字节流按 whole / bytes:1 / random:7 三种切法喂入，
 * 事件序列 JSON 必须完全相同（贴三次输出的 sha256）。
 */
class ByteSlicingDeterminismTest {

    private val sample: ByteArray = Sse.bytes(
        Sse.comment("keep-alive"),
        Sse.content("第一段"),
        Sse.reasoning("先看用户意图"),
        Sse.contentAndReasoning("答", "推理"),
        Sse.toolCall(0, "call_a1", "web_search", "{\"q\":"),
        Sse.toolCall(0, null, null, "\"天气\"}"),
        Sse.comment("OPENROUTER PROCESSING"),
        Sse.toolCall(1, "call_b2", "calc", "{}"),
        Sse.content("两段\n换行\t与\"引号\"", finishReason = "tool_calls"),
        Sse.usage(412, 37),
        Sse.done(),
    )

    @Test
    fun `三种切包方式产出逐字节相同的事件序列`() {
        val whole = runSession(sample, pattern = ChunkPattern.Whole)
        val perByte = runSession(sample, pattern = ChunkPattern.Bytes(1))
        val random = runSession(sample, pattern = ChunkPattern.RandomSlices(7))

        val wholeHash = sha256(whole.lines)
        val perByteHash = sha256(perByte.lines)
        val randomHash = sha256(random.lines)

        println("=== SLICE whole   sha256=$wholeHash events=${whole.events.size} ===")
        println(whole.lines)
        println("=== SLICE bytes:1 sha256=$perByteHash events=${perByte.events.size} ===")
        println("=== SLICE random:7 sha256=$randomHash events=${random.events.size} ===")

        assertEquals(whole.lines, perByte.lines, "whole 与 bytes:1 输出不同")
        assertEquals(whole.lines, random.lines, "whole 与 random:7 输出不同")
        assertEquals(wholeHash, perByteHash)
        assertEquals(wholeHash, randomHash)
        assertEquals(12, whole.events.size)
        assertEquals((0L until 12L).toList(), whole.events.map { it.seq })
    }

    @Test
    fun `事件序列是稳定的 JSON Lines 无空行无尾随逗号`() {
        val result = runSession(sample, pattern = ChunkPattern.Bytes(5))
        val lines = result.lines.trimEnd().split("\n")
        println("DETERMINISM lines=${lines.size}")
        lines.forEach { line ->
            assertEquals(line, Json.write(Json.parse(line)), "事件行不是规范化 JSON：$line")
            assertEquals(true, line.startsWith("{") && line.endsWith("}"))
        }
        assertEquals(result.events.size, lines.size)
    }
}
