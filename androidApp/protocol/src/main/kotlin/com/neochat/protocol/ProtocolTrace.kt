package com.neochat.protocol

/** 协议层 trace 计数器：只用于诊断，绝不进入 StreamEvent（否则会改变 seq）。 */
public class ProtocolTrace {

    private val counters: MutableMap<String, Long> = LinkedHashMap()

    @Synchronized
    public fun count(name: String, delta: Long = 1L) {
        counters[name] = (counters[name] ?: 0L) + delta
    }

    @Synchronized
    public fun value(name: String): Long = counters[name] ?: 0L

    @Synchronized
    public fun snapshot(): Map<String, Long> = counters.toSortedMap()
}
