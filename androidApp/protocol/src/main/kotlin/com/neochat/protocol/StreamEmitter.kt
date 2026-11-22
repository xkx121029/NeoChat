package com.neochat.protocol

import com.neochat.protocol.errors.ErrorObject

/**
 * 事件发射器：唯一的 seq 分配点，并强制 I1..I8 的结构性部分。
 * 终止事件发出后立刻回调 [onTerminate]，供传输层关闭 socket（I12）。
 */
public class StreamEmitter(
    private val trace: ProtocolTrace,
    private val sink: (StreamEvent) -> Unit,
    private val onTerminate: () -> Unit = {},
) {

    private var seq: Long = 0L
    private var terminated: Boolean = false
    private var textChars: Int = 0
    private var reasoningChars: Int = 0
    private var usageEmitted: Boolean = false
    private val opened: MutableSet<Int> = java.util.TreeSet()
    private val closed: MutableSet<Int> = HashSet()

    public fun isTerminated(): Boolean = terminated

    public fun emittedTextLength(): Int = textChars

    public fun emittedReasoningLength(): Int = reasoningChars

    public fun delta(text: String) {
        if (terminated || text.isEmpty()) return
        sink(StreamEvent.Delta(seq++, text))
        textChars += text.length
    }

    public fun reasoning(text: String) {
        if (terminated || text.isEmpty()) return
        sink(StreamEvent.ReasoningDelta(seq++, text))
        reasoningChars += text.length
    }

    public fun toolCallDelta(index: Int, id: String?, name: String?, args: String?) {
        if (terminated || closed.contains(index)) return
        val firstFragment = !opened.contains(index)
        if (firstFragment) {
            opened.add(index)
        } else {
            if (id != null) trace.count("tool_call_id_repeated")
            if (name != null) trace.count("tool_call_name_repeated")
        }
        if (firstFragment && name == null) trace.count("tool_delta_missing_name")
        val emitId = if (firstFragment) id ?: "call_$index" else null
        val emitName = if (firstFragment) name ?: "" else null
        val emitArgs = args ?: if (firstFragment) "" else null
        if (!firstFragment && emitArgs == null) return
        sink(StreamEvent.ToolCallDelta(seq++, index, emitId, emitName, emitArgs))
    }

    public fun toolCallEnd(index: Int) {
        if (terminated) return
        if (!opened.contains(index)) {
            trace.count("tool_call_end_without_open")
            return
        }
        if (!closed.add(index)) {
            trace.count("tool_call_end_duplicate")
            return
        }
        sink(StreamEvent.ToolCallEnd(seq++, index))
    }

    public fun usage(counts: UsageCounts) {
        if (terminated) return
        if (usageEmitted) {
            trace.count("usage_duplicate")
            return
        }
        usageEmitted = true
        sink(StreamEvent.Usage(seq++, counts))
    }

    public fun notice(code: NoticeCode, params: Map<String, List<String>> = emptyMap()) {
        if (terminated) return
        sink(StreamEvent.Notice(seq++, code, params))
    }

    public fun done(finishReason: FinishReason, rawFinishReason: String? = null) {
        if (terminated) return
        val resolved = if (finishReason == FinishReason.TOOL_CALLS && opened.isEmpty()) {
            trace.count("tool_calls_without_index")
            FinishReason.STOP
        } else {
            finishReason
        }
        for (index in opened.toSortedSet()) {
            if (closed.add(index)) sink(StreamEvent.ToolCallEnd(seq++, index))
        }
        sink(StreamEvent.Done(seq++, resolved, rawFinishReason))
        terminate()
    }

    public fun failed(error: ErrorObject) {
        if (terminated) return
        sink(StreamEvent.Failed(seq++, error))
        terminate()
    }

    private fun terminate() {
        terminated = true
        onTerminate()
    }
}
