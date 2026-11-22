package com.neochat.protocol

import com.neochat.protocol.errors.ProviderFacts
import com.neochat.protocol.sse.SseFramer

/** 一次流的适配器配置。`duplicateChunks` 对应 conformance 的 `meta.duplicate`，`cumulativeStream` 对应 provider 列。 */
public data class StreamConfig(
    public val provider: String? = null,
    public val model: String? = null,
    public val maxFrameBytes: Long = SseFramer.DEFAULT_MAX_FRAME_BYTES,
    public val duplicateChunks: Boolean = false,
    public val cumulativeStream: Boolean = false,
    public val billingUrl: String? = null,
    public val statusUrl: String? = null,
) {

    public fun facts(): ProviderFacts = ProviderFacts(
        provider = provider,
        model = model,
        billingUrl = billingUrl,
        statusUrl = statusUrl,
    )

    /** stream-events.md §4.1：仅 meta.duplicate 或 provider.cumulative_stream 才做前缀差集。 */
    internal fun dedupeByPrefix(): Boolean = duplicateChunks || cumulativeStream
}
