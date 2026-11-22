package com.neochat.protocol.openai

import com.neochat.protocol.FinishReason
import com.neochat.protocol.Json
import com.neochat.protocol.JsonParseException
import com.neochat.protocol.JsonValue
import com.neochat.protocol.NoticeCode
import com.neochat.protocol.ProtocolTrace
import com.neochat.protocol.ProviderCost
import com.neochat.protocol.StreamConfig
import com.neochat.protocol.StreamEmitter
import com.neochat.protocol.UsageCounts
import com.neochat.protocol.asObjOrNull
import com.neochat.protocol.errors.ErrorMapper
import com.neochat.protocol.field
import com.neochat.protocol.longOrNull
import com.neochat.protocol.numberRaw
import com.neochat.protocol.stringOrNull

/** OpenAI Chat Completions（含 OpenAI 兼容、OpenRouter）帧 → StreamEvent，见 stream-events.md §4.1。 */
internal class OpenAiChatMapper(
    private val config: StreamConfig,
    private val emitter: StreamEmitter,
    private val trace: ProtocolTrace,
) {

    private var pendingFinish: FinishReason? = null
    private var rawFinish: String? = null
    private var sawFinishReason: Boolean = false
    private var multiChoiceNotified: Boolean = false
    private val emittedPrefix: StringBuilder = StringBuilder()

    fun onPayload(payload: String) {
        if (payload == "[DONE]") {
            finish()
            return
        }
        val root = Json.parse(payload).asObjOrNull()
            ?: throw JsonParseException("SSE payload is not a JSON object")
        if (root.field("error") != null) {
            emitter.failed(ErrorMapper.fromStreamPayload(payload, config.facts()))
            return
        }
        val choices = (root.field("choices") as? JsonValue.Arr)?.items.orEmpty()
        choices.mapNotNull { it.asObjOrNull() }
            .firstOrNull { it.field("index")?.longOrNull()?.let { index -> index == 0L } ?: true }
            ?.let { onChoice(it) }
        if (choices.size > 1) notifyMultiChoice()
        root.field("usage")?.asObjOrNull()?.let { onUsage(it) }
    }

    fun onEndOfStream() {
        if (emitter.isTerminated()) return
        if (sawFinishReason) {
            trace.count("done_on_eof")
            emitter.done(pendingFinish ?: FinishReason.STOP, rawFinish)
        } else {
            emitter.failed(ErrorMapper.parseIncomplete(config.facts(), emitter.emittedTextLength()))
        }
    }

    private fun onChoice(choice: JsonValue.Obj) {
        choice.field("delta")?.asObjOrNull()?.let { onDelta(it) }
        choice.field("finish_reason")?.stringOrNull()?.let { raw -> recordFinish(raw) }
    }

    private fun onDelta(delta: JsonValue.Obj) {
        delta.field("content")?.stringOrNull()?.let { emitContent(it) }
        reasoningText(delta)?.let { emitter.reasoning(it) }
        (delta.field("tool_calls") as? JsonValue.Arr)?.items.orEmpty()
            .mapNotNull { it.asObjOrNull() }
            .sortedBy { it.field("index")?.longOrNull() ?: 0L }
            .forEach { onToolCall(it) }
    }

    private fun reasoningText(delta: JsonValue.Obj): String? {
        val reasoningContent = delta.field("reasoning_content")?.stringOrNull()
        val reasoning = delta.field("reasoning")?.stringOrNull()
        if (reasoningContent != null && reasoning != null) trace.count("reasoning_field_conflict")
        return reasoningContent ?: reasoning
    }

    private fun emitContent(content: String) {
        if (content.isEmpty()) return
        emitter.delta(if (config.dedupeByPrefix()) dedupe(content) else content)
    }

    private fun dedupe(content: String): String {
        val prefix = emittedPrefix.toString()
        if (prefix.isEmpty()) {
            emittedPrefix.append(content)
            return content
        }
        if (!content.startsWith(prefix)) {
            trace.count("cumulative_rewrite")
            emittedPrefix.setLength(0)
            emittedPrefix.append(content)
            return content
        }
        val suffix = content.substring(prefix.length)
        if (suffix.isEmpty()) {
            trace.count("duplicate_chunk_ignored")
        } else {
            emittedPrefix.append(suffix)
        }
        return suffix
    }

    private fun onToolCall(call: JsonValue.Obj) {
        val index = call.field("index")?.longOrNull()?.toInt() ?: 0
        val function = call.field("function")?.asObjOrNull()
        emitter.toolCallDelta(
            index = index,
            id = call.field("id")?.stringOrNull(),
            name = function?.field("name")?.stringOrNull(),
            args = function?.field("arguments")?.stringOrNull(),
        )
    }

    private fun recordFinish(raw: String) {
        sawFinishReason = true
        rawFinish = raw
        pendingFinish = when (raw) {
            "stop" -> FinishReason.STOP
            "length" -> FinishReason.LENGTH
            "tool_calls", "function_call" -> FinishReason.TOOL_CALLS
            "content_filter" -> FinishReason.CONTENT_FILTER
            else -> {
                trace.count("finish_reason_unmapped")
                null
            }
        }
    }

    private fun finish() {
        if (pendingFinish == null) trace.count("done_without_finish_reason")
        emitter.done(pendingFinish ?: FinishReason.STOP, rawFinish)
    }

    private fun onUsage(usage: JsonValue.Obj) {
        val detailCached = usage.field("prompt_tokens_details")?.asObjOrNull()
        val detailReasoning = usage.field("completion_tokens_details")?.asObjOrNull()
        emitter.usage(
            UsageCounts(
                input = usage.field("prompt_tokens")?.longOrNull() ?: 0L,
                output = usage.field("completion_tokens")?.longOrNull() ?: 0L,
                cached = detailCached?.field("cached_tokens")?.longOrNull(),
                reasoning = detailReasoning?.field("reasoning_tokens")?.longOrNull(),
                total = usage.field("total_tokens")?.longOrNull(),
                estimated = false,
                providerCost = usage.field("cost")?.numberRaw()?.let { ProviderCost(it, "USD") },
            ),
        )
    }

    private fun notifyMultiChoice() {
        if (multiChoiceNotified) return
        multiChoiceNotified = true
        trace.count("multi_choice_ignored")
        emitter.notice(NoticeCode.MULTI_CHOICE_IGNORED)
    }
}
