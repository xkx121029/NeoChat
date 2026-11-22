package com.neochat.protocol.transport

import com.neochat.protocol.FinishReason
import com.neochat.protocol.StreamEvent

/** stream-events.md §2.2 的计时器（connect / first_byte / read_idle；total 由调用方另管）。 */
public data class StreamTimeouts(
    public val connectMs: Long = 10_000L,
    public val firstByteMs: Long = 60_000L,
    public val readIdleMs: Long = 180_000L,
)

/** 一个活跃流的句柄：取消、等待终止、取事件与 trace。 */
public interface StreamHandle {

    /** 取消：MUST 关闭底层 socket，然后补 done{aborted_user}（stream-events.md §5.1）。 */
    public fun cancel()

    public fun awaitTermination(timeoutMs: Long): Boolean

    public fun isTerminated(): Boolean

    public fun events(): List<StreamEvent>

    public fun trace(): Map<String, Long>
}

/** 供测试与诊断使用：判断事件流是否以 aborted_user 收尾。 */
public fun List<StreamEvent>.endedWithUserAbort(): Boolean =
    lastOrNull()?.let { it is StreamEvent.Done && it.finishReason == FinishReason.ABORTED_USER } == true
