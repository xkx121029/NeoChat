package com.neochat.protocol

import com.neochat.protocol.request.AttachmentBytes
import com.neochat.protocol.request.ChatMessage
import com.neochat.protocol.request.ChatRequest
import com.neochat.protocol.request.ChatRole
import com.neochat.protocol.request.MessagePart
import com.neochat.protocol.request.OpenAiChatProvider
import com.neochat.protocol.request.OpenAiChatRequestBuilder
import com.neochat.protocol.request.Reasoning
import com.neochat.protocol.request.ResponseFormat
import com.neochat.protocol.request.Sampling
import com.neochat.protocol.request.ToolChoice
import com.neochat.protocol.request.ToolSpec
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/** providers.md §3.2：OpenAI Chat 请求构造（base_url、header、body）。 */
class RequestBuilderTest {

    private val provider = OpenAiChatProvider(
        baseUrl = "https://api.example.com/v1/",
        apiKey = "sk-test-key-1234567890",
    )

    @Test
    fun `base_url 尾部斜杠被去掉并补 chat completions`() {
        println("ENDPOINT 1 = ${OpenAiChatRequestBuilder.endpoint(provider)}")
        assertEquals("https://api.example.com/v1/chat/completions", OpenAiChatRequestBuilder.endpoint(provider))
        assertEquals(
            "https://relay.example.com/chat/completions",
            OpenAiChatRequestBuilder.endpoint(OpenAiChatProvider(baseUrl = "https://relay.example.com", apiKey = "k")),
        )
    }

    @Test
    fun `base_url 已含标准后缀时不重复拼接且支持自定义 query`() {
        val withSuffix = OpenAiChatProvider(baseUrl = "https://gw.example.com/openai/chat/completions", apiKey = "k")
        println("ENDPOINT 2 = ${OpenAiChatRequestBuilder.endpoint(withSuffix)}")
        assertEquals("https://gw.example.com/openai/chat/completions", OpenAiChatRequestBuilder.endpoint(withSuffix))
        val withQuery = withSuffix.copy(extraQuery = mapOf("api-version" to "2026-01-01", "b" to "2"))
        assertEquals(
            "https://gw.example.com/openai/chat/completions?api-version=2026-01-01&b=2",
            OpenAiChatRequestBuilder.endpoint(withQuery),
        )
    }

    @Test
    fun `默认发 Bearer 与 SSE 头 自定义认证头会抑制 Authorization`() {
        val headers = OpenAiChatRequestBuilder.headers(provider)
        println("HEADERS default = $headers")
        assertEquals("Bearer sk-test-key-1234567890", headers["Authorization"])
        assertEquals("application/json", headers["Content-Type"])
        assertEquals("text/event-stream", headers["Accept"])

        val gateway = provider.copy(extraHeaders = mapOf("x-api-key" to "gw-secret", "X-Title" to "NeoChat"))
        val gatewayHeaders = OpenAiChatRequestBuilder.headers(gateway)
        println("HEADERS gateway = $gatewayHeaders")
        assertNull(gatewayHeaders["Authorization"])
        assertEquals("gw-secret", gatewayHeaders["x-api-key"])
        assertEquals("NeoChat", gatewayHeaders["X-Title"])
    }

    @Test
    fun `body 含 stream 与 include_usage 且 system 进首条消息`() {
        val request = ChatRequest(
            model = "gpt-4o-mini",
            system = "You are NeoChat.",
            messages = listOf(
                ChatMessage(ChatRole.USER, listOf(MessagePart.Text("你好"))),
                ChatMessage(
                    ChatRole.ASSISTANT,
                    listOf(
                        MessagePart.Text("我来查"),
                        MessagePart.ToolCall("call_a1", "web_search", "{\"q\":\"猫\"}"),
                    ),
                ),
                ChatMessage(
                    ChatRole.TOOL,
                    listOf(MessagePart.ToolResult(true, "{\"items\":[]}")),
                    toolCallId = "call_a1",
                ),
            ),
            sampling = Sampling(temperature = 0.7, topP = 1.0, maxOutputTokens = 2048, stop = listOf("\n\nHuman:")),
            reasoning = Reasoning(mode = "effort", effort = "high"),
        )
        val spec = OpenAiChatRequestBuilder.build(request, provider)
        val body = Json.parse(spec.body.toString(Charsets.UTF_8))
        println("BODY = ${spec.body.toString(Charsets.UTF_8)}")
        assertEquals("gpt-4o-mini", body.field("model")?.stringOrNull())
        assertEquals(true, body.field("stream")?.boolOrNull())
        assertEquals(true, body.field("stream_options")?.field("include_usage")?.boolOrNull())
        assertEquals("high", body.field("reasoning_effort")?.stringOrNull())
        val messages = (body.field("messages") as JsonValue.Arr).items
        assertEquals(4, messages.size)
        assertEquals("system", messages[0].field("role")?.stringOrNull())
        assertEquals("You are NeoChat.", messages[0].field("content")?.stringOrNull())
        assertEquals("user", messages[1].field("role")?.stringOrNull())
        assertEquals("你好", messages[1].field("content")?.stringOrNull())
        val assistant = messages[2]
        val toolCalls = (assistant.field("tool_calls") as JsonValue.Arr).items
        assertEquals("call_a1", toolCalls[0].field("id")?.stringOrNull())
        assertEquals("web_search", toolCalls[0].field("function")?.field("name")?.stringOrNull())
        assertEquals("{\"q\":\"猫\"}", toolCalls[0].field("function")?.field("arguments")?.stringOrNull())
        assertEquals("call_a1", messages[3].field("tool_call_id")?.stringOrNull())
        assertEquals("0.7", body.field("temperature")?.numberRaw())
        assertEquals(2048L, body.field("max_tokens")?.longOrNull())
    }

    @Test
    fun `图片 part 转 data URL 且工具定义与 tool_choice 正确`() {
        val request = ChatRequest(
            model = "gpt-4o-mini",
            messages = listOf(
                ChatMessage(
                    ChatRole.USER,
                    listOf(
                        MessagePart.Text("看图"),
                        MessagePart.Image("image/png", "att_01H", "auto"),
                    ),
                ),
            ),
            tools = listOf(ToolSpec("web_search", "联网", "{\"type\":\"object\"}")),
            toolChoice = ToolChoice.Named("web_search"),
            responseFormat = ResponseFormat.JsonObject,
        )
        val body = OpenAiChatRequestBuilder.body(request, provider) { ref ->
            assertEquals("att_01H", ref)
            AttachmentBytes("image/png", "aGVsbG8=")
        }
        println("BODY image = ${Json.write(body)}")
        val content = (body.field("messages")?.let { (it as JsonValue.Arr).items[0] }?.field("content")) as JsonValue.Arr
        assertEquals("text", content.items[0].field("type")?.stringOrNull())
        assertEquals("image_url", content.items[1].field("type")?.stringOrNull())
        assertEquals(
            "data:image/png;base64,aGVsbG8=",
            content.items[1].field("image_url")?.field("url")?.stringOrNull(),
        )
        val tool = (body.field("tools") as JsonValue.Arr).items[0]
        assertEquals("function", tool.field("type")?.stringOrNull())
        assertEquals("web_search", tool.field("function")?.field("name")?.stringOrNull())
        val choice = body.field("tool_choice")!!
        assertEquals("web_search", choice.field("function")?.field("name")?.stringOrNull())
        assertEquals("json_object", body.field("response_format")?.field("type")?.stringOrNull())
    }

    @Test
    fun `max_completion_tokens 字段可由能力矩阵切换`() {
        val request = ChatRequest(model = "o5", sampling = Sampling(maxOutputTokens = 100))
        val body = OpenAiChatRequestBuilder.body(request, provider.copy(maxTokensField = "max_completion_tokens"))
        println("BODY max_tokens field = ${Json.write(body)}")
        assertEquals(100L, body.field("max_completion_tokens")?.longOrNull())
        assertNull(body.field("max_tokens"))
    }

    @Test
    fun `extra_body_json 合并进请求体顶层且请求字节可重复复用`() {
        val request = ChatRequest(model = "m", messages = listOf(ChatMessage(ChatRole.USER, listOf(MessagePart.Text("x")))))
        val spec1 = OpenAiChatRequestBuilder.build(request, provider.copy(extraBodyJson = "{\"top_k\":5}"))
        val spec2 = OpenAiChatRequestBuilder.build(request, provider.copy(extraBodyJson = "{\"top_k\":5}"))
        println("BODY extra = ${spec1.body.toString(Charsets.UTF_8)}")
        assertEquals(5L, Json.parse(spec1.body.toString(Charsets.UTF_8)).field("top_k")?.longOrNull())
        assertTrue(spec1.body.contentEquals(spec2.body), "同一请求快照必须字节一致（重试复用）")
        assertEquals("application/json", spec1.header("Content-Type"))
    }
}
