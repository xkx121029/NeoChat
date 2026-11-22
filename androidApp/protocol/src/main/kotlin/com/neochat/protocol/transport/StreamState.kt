package com.neochat.protocol.transport

import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong
import okhttp3.Call
import okhttp3.Response

/**
 * 一次传输的状态：首字节时刻、最后一次任何字节的时刻、关闭标志、终止闩锁。
 * "任何字节"包含心跳注释，因此 read_idle 不会被 Anthropic 的 ping 误判（stream-events.md §2.2）。
 */
internal class StreamState {

    @Volatile
    var call: Call? = null

    @Volatile
    var response: Response? = null

    @Volatile
    var cancelRequested: Boolean = false

    private val startedAt: Long = System.currentTimeMillis()
    private val lastActivity: AtomicLong = AtomicLong(startedAt)
    private val firstByteAt: AtomicLong = AtomicLong(0L)
    private val latch: CountDownLatch = CountDownLatch(1)
    private val closed: AtomicBoolean = AtomicBoolean(false)

    fun startedAt(): Long = startedAt

    fun touch() {
        lastActivity.set(System.currentTimeMillis())
    }

    fun lastActivityAt(): Long = lastActivity.get()

    fun markFirstByte() {
        firstByteAt.compareAndSet(0L, System.currentTimeMillis())
        touch()
    }

    fun firstByteSeen(): Boolean = firstByteAt.get() > 0L

    fun isClosed(): Boolean = closed.get()

    fun release() {
        if (closed.compareAndSet(false, true)) latch.countDown()
    }

    fun awaitTermination(timeoutMs: Long): Boolean = latch.await(timeoutMs, TimeUnit.MILLISECONDS)

    /** 关闭响应体并取消 call：这一步才是"连接真的关了"，而不是只停止读取（I12）。 */
    fun closeConnection() {
        runCatching { response?.close() }
        call?.cancel()
    }
}
