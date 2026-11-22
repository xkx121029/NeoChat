package com.neochat.protocol

import com.neochat.protocol.errors.ErrorMapper
import com.neochat.protocol.errors.ProviderFacts
import com.neochat.protocol.errors.Redactor
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/** 验收第 5 条：脱敏管线（errors.md §7）逐条断言。 */
class RedactionTest {

    @Test
    fun `Authorization 头整体替换为 redacted`() {
        val input = "Authorization: Bearer sk-abcdefghijklmnopqrstuvwxyz012345"
        val output = Redactor.redact(input)
        println("REDACT authorization: [$input] -> [$output]")
        assertEquals("Authorization: [redacted]", output)
        assertFalse(output.contains("sk-abc"))
    }

    @Test
    fun `x-api-key 头整体替换为 redacted`() {
        val input = "x-api-key: sk-proj-1234567890abcdefghijklmn"
        val output = Redactor.redact(input)
        println("REDACT x-api-key: [$input] -> [$output]")
        assertEquals("x-api-key: [redacted]", output)
    }

    @Test
    fun `query 中的 key 参数替换为 redacted`() {
        val input = "https://relay.example.com/v1/chat/completions?key=sk-abcd1234efgh5678&foo=1"
        val output = Redactor.redact(input)
        println("REDACT query: [$input] -> [$output]")
        assertEquals("https://relay.example.com/v1/chat/completions?key=[redacted]&foo=1", output)
    }

    @Test
    fun `sk 形态串保留首4与尾4`() {
        val input = "Incorrect API key provided: sk-abcdefghijklmnop1234"
        val output = Redactor.redact(input)
        println("REDACT sk: [$input] -> [$output]")
        assertTrue(output.contains("sk-a\u20261234"), "期望首4+…+尾4，实际 [$output]")
        assertFalse(output.contains("sk-abcdefghijklmnop"))
    }

    @Test
    fun `其它 provider key 形态同样脱敏`() {
        val samples = mapOf(
            "AIzaSyD-1234567890abcdefghijklmnop" to "AIza\u2026mnop",
            "gsk_abcdefghijklmnopqrstuvwx" to "gsk_\u2026uvwx",
            "xai-1234567890abcdefghij" to "xai-\u2026ghij",
        )
        samples.forEach { (key, masked) ->
            val output = Redactor.redact("token=$key")
            println("REDACT form: [$key] -> [$output]")
            assertTrue(output.contains(masked), "期望 [$masked]，实际 [$output]")
        }
    }

    @Test
    fun `邮箱保留首字符与域名`() {
        val output = Redactor.redact("contact john.doe@example.com now")
        println("REDACT email: [contact john.doe@example.com now] -> [$output]")
        assertEquals("contact j***example.com now", output)
    }

    @Test
    fun `本地绝对路径中的用户名替换为波浪号`() {
        val windows = Redactor.redact("C:\\Users\\alice\\NeoChat\\runs\\stream.sse")
        val posix = Redactor.redact("/home/bob/neochat/stream.sse")
        val mac = Redactor.redact("/Users/carol/neochat/stream.sse")
        println("REDACT path: [$windows] [$posix] [$mac]")
        assertEquals("C:\\Users\\~\\NeoChat\\runs\\stream.sse", windows)
        assertEquals("/home/~/neochat/stream.sse", posix)
        assertEquals("/Users/~/neochat/stream.sse", mac)
    }

    @Test
    fun `管线顺序固定先脱敏再按 UTF-8 边界截断到 512 字节`() {
        val tail = "a".repeat(600)
        val output = Redactor.redact("sk-abcdefghijklmnop1234 $tail")
        val bytes = output.toByteArray(Charsets.UTF_8)
        println("REDACT order: length=${output.length} bytes=${bytes.size} head=${output.take(16)}")
        assertTrue(bytes.size <= 512, "截断后仍有 ${bytes.size} 字节")
        assertTrue(output.startsWith("sk-a\u20261234"), "先截断会把半个 key 留在边界处：[$output]")
        assertFalse(output.contains("sk-abcdefghijklmnop"))
    }

    @Test
    fun `截断不切坏多字节字符`() {
        val input = "a".repeat(511) + "你"
        val output = Redactor.redact(input)
        val bytes = output.toByteArray(Charsets.UTF_8)
        println("REDACT utf8: chars=${output.length} bytes=${bytes.size}")
        assertTrue(bytes.size <= 512)
        assertEquals(511, output.length)
    }

    @Test
    fun `错误对象的 raw 与 hint 均已脱敏`() {
        val body = "{\"error\":{\"message\":\"Incorrect API key provided: sk-abcdefghijklmnop1234\", " +
            "\"code\":\"invalid_api_key\"}}"
        val error = ErrorMapper.fromHttp(401, emptyMap(), body, ProviderFacts(provider = "openai_compatible"))
        val raw = error.raw!!
        println("REDACT error raw=[$raw] params=${error.params}")
        assertFalse(raw.contains("sk-abcdefghijklmnop"))
        assertTrue(raw.toByteArray(Charsets.UTF_8).size <= 512)
        val hint = Json.write(error.params.getValue("hint"))
        assertEquals("\"sk-a\u20261234\"", hint)
    }
}
