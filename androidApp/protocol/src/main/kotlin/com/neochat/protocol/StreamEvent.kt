package com.neochat.protocol

import com.neochat.protocol.errors.ErrorObject

/** stream-events.md §3.2 的 finish_reason 闭集。 */
public enum class FinishReason(public val wire: String) {
    STOP("stop"),
    LENGTH("length"),
    TOOL_CALLS("tool_calls"),
    CONTENT_FILTER("content_filter"),
    ABORTED_USER("aborted_user"),
}

/** stream-events.md §3.2 的 notice.code 闭集。 */
public enum class NoticeCode(public val wire: String) {
    CAPABILITY_STRIPPED("capability_stripped"),
    INPUT_TRUNCATED("input_truncated"),
    MULTI_CHOICE_IGNORED("multi_choice_ignored"),
    USAGE_ESTIMATED("usage_estimated"),
    REASONING_UNSUPPORTED("reasoning_unsupported"),
    PROVIDER_DOWNGRADED("provider_downgraded"),
    STREAM_INTERRUPTED("stream_interrupted"),
}

public data class ProviderCost(public val amount: String, public val currency: String) {

    public fun toJson(): JsonValue.Obj = Json.obj(
        "amount" to JsonValue.Str(amount),
        "currency" to JsonValue.Str(currency),
    )
}

public data class UsageCounts(
    public val input: Long,
    public val output: Long,
    public val cached: Long? = null,
    public val reasoning: Long? = null,
    public val total: Long? = null,
    public val estimated: Boolean? = null,
    public val errorBoundPct: Double? = null,
    public val providerCost: ProviderCost? = null,
) {

    public fun toJson(): JsonValue.Obj = Json.obj(
        "input" to JsonValue.Num(input.toString()),
        "output" to JsonValue.Num(output.toString()),
        "cached" to cached?.let { JsonValue.Num(it.toString()) },
        "reasoning" to reasoning?.let { JsonValue.Num(it.toString()) },
        "total" to total?.let { JsonValue.Num(it.toString()) },
        "estimated" to estimated?.let { JsonValue.Bool(it) },
        "error_bound_pct" to errorBoundPct?.let { JsonValue.Num(it.toString()) },
        "provider_cost" to providerCost?.toJson(),
    )
}

/** L2 归一化输出：JSON Lines 的一行一个事件。 */
public sealed interface StreamEvent {

    public val seq: Long

    public fun toJson(): JsonValue.Obj

    public fun toJsonLine(): String = Json.write(toJson())

    public data class Delta(override val seq: Long, public val text: String) : StreamEvent {
        override fun toJson(): JsonValue.Obj = Json.obj(
            "t" to JsonValue.Str("delta"),
            "seq" to JsonValue.Num(seq.toString()),
            "text" to JsonValue.Str(text),
        )
    }

    public data class ReasoningDelta(override val seq: Long, public val text: String) : StreamEvent {
        override fun toJson(): JsonValue.Obj = Json.obj(
            "t" to JsonValue.Str("reasoning_delta"),
            "seq" to JsonValue.Num(seq.toString()),
            "text" to JsonValue.Str(text),
        )
    }

    public data class ToolCallDelta(
        override val seq: Long,
        public val index: Int,
        public val id: String? = null,
        public val name: String? = null,
        public val args: String? = null,
    ) : StreamEvent {
        override fun toJson(): JsonValue.Obj = Json.obj(
            "t" to JsonValue.Str("tool_call_delta"),
            "seq" to JsonValue.Num(seq.toString()),
            "index" to JsonValue.Num(index.toString()),
            "id" to id?.let { JsonValue.Str(it) },
            "name" to name?.let { JsonValue.Str(it) },
            "args" to args?.let { JsonValue.Str(it) },
        )
    }

    public data class ToolCallEnd(override val seq: Long, public val index: Int) : StreamEvent {
        override fun toJson(): JsonValue.Obj = Json.obj(
            "t" to JsonValue.Str("tool_call_end"),
            "seq" to JsonValue.Num(seq.toString()),
            "index" to JsonValue.Num(index.toString()),
        )
    }

    public data class Usage(override val seq: Long, public val counts: UsageCounts) : StreamEvent {
        override fun toJson(): JsonValue.Obj {
            val fields = LinkedHashMap<String, JsonValue>()
            fields["t"] = JsonValue.Str("usage")
            fields["seq"] = JsonValue.Num(seq.toString())
            val countsJson = counts.toJson()
            countsJson.fields.forEach { (key, value) -> fields[key] = value }
            return JsonValue.Obj(fields)
        }
    }

    public data class Notice(
        override val seq: Long,
        public val code: NoticeCode,
        public val params: Map<String, List<String>> = emptyMap(),
    ) : StreamEvent {
        override fun toJson(): JsonValue.Obj = Json.obj(
            "t" to JsonValue.Str("notice"),
            "seq" to JsonValue.Num(seq.toString()),
            "code" to JsonValue.Str(code.wire),
            "params" to paramsJson(params),
        )

        private fun paramsJson(params: Map<String, List<String>>): JsonValue {
            val fields = params.toSortedMap().mapValues { (_, values) ->
                if (values.size == 1) {
                    JsonValue.Str(values.first())
                } else {
                    JsonValue.Arr(values.map { JsonValue.Str(it) })
                }
            }
            return JsonValue.Obj(fields)
        }
    }

    public data class Done(
        override val seq: Long,
        public val finishReason: FinishReason,
        public val rawFinishReason: String? = null,
    ) : StreamEvent {
        override fun toJson(): JsonValue.Obj = Json.obj(
            "t" to JsonValue.Str("done"),
            "seq" to JsonValue.Num(seq.toString()),
            "finish_reason" to JsonValue.Str(finishReason.wire),
            "raw_finish_reason" to rawFinishReason?.let { JsonValue.Str(it) },
        )
    }

    public data class Failed(override val seq: Long, public val error: ErrorObject) : StreamEvent {
        override fun toJson(): JsonValue.Obj = Json.obj(
            "t" to JsonValue.Str("failed"),
            "seq" to JsonValue.Num(seq.toString()),
            "error" to error.toJson(),
        )
    }
}

/** runner 级工具：事件序列 → JSON Lines 文本（每行一个事件，末尾换行）。 */
public fun List<StreamEvent>.toJsonLines(): String = joinToString(separator = "") { it.toJsonLine() + "\n" }
