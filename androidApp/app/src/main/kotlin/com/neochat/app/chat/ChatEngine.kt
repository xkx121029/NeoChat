package com.neochat.app.chat

import com.neochat.protocol.StreamConfig
import com.neochat.protocol.StreamEvent
import com.neochat.protocol.request.ChatRequest
import com.neochat.protocol.request.OpenAiChatProvider
import com.neochat.protocol.request.OpenAiChatRequestBuilder
import com.neochat.protocol.transport.SseStreamClient

/** 一个活跃流的取消句柄。 */
public fun interface ChatRun {
    public fun cancel()
}

/** 请求层接口：界面层只认这个，不认 OkHttp、不认 SSE。 */
public interface ChatEngine {
    /** 开一次流式请求；`onEvent` 可能在任意线程被调用。 */
    public fun start(request: ChatRequest, onEvent: (StreamEvent) -> Unit): ChatRun

    public fun close()
}

/**
 * 接已完成的协议层：`OpenAiChatRequestBuilder` 构造请求快照，
 * `SseStreamClient` 负责传输、计时、取消，回调里已经是归一化后的 `StreamEvent`。
 */
public class ProtocolChatEngine(
    private val provider: () -> ProviderConfig,
    private val client: SseStreamClient = SseStreamClient(),
) : ChatEngine {

    override fun start(request: ChatRequest, onEvent: (StreamEvent) -> Unit): ChatRun {
        val config = provider()
        val spec = OpenAiChatRequestBuilder.build(
            request = request,
            provider = OpenAiChatProvider(baseUrl = config.baseUrl, apiKey = config.apiKey),
        )
        val handle = client.open(
            spec = spec,
            config = StreamConfig(provider = PROVIDER_ID, model = request.model),
            onEvent = onEvent,
        )
        return ChatRun { handle.cancel() }
    }

    override fun close() {
        client.close()
    }

    private companion object {
        val PROVIDER_ID: String = ProviderConfigStore.PROVIDER_ID
    }
}
