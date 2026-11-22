package com.neochat.protocol.support

import java.io.BufferedInputStream
import java.io.ByteArrayOutputStream
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import java.util.concurrent.atomic.AtomicReference

internal class RawRequest(
    val requestLine: String,
    val headers: Map<String, String>,
    val body: ByteArray,
) {

    val method: String = requestLine.split(" ").firstOrNull() ?: ""

    val path: String = requestLine.split(" ").getOrElse(1) { "/" }

    val bodyText: String = body.toString(Charsets.UTF_8)

    fun header(name: String): String? =
        headers.entries.firstOrNull { it.key.equals(name, ignoreCase = true) }?.value
}

/** 原始 socket 应答端：完全控制字节与关闭时序，才能验证"连接真的关了"（I12/取消）。 */
internal class RawSink(private val socket: Socket) {

    private val out: OutputStream = socket.getOutputStream()

    fun head(status: Int, headers: Map<String, String>) {
        val sb = StringBuilder("HTTP/1.1 $status ${reason(status)}\r\n")
        headers.forEach { (key, value) -> sb.append(key).append(": ").append(value).append("\r\n") }
        sb.append("\r\n")
        out.write(sb.toString().toByteArray(Charsets.ISO_8859_1))
        out.flush()
    }

    fun eventStream(extraHeaders: Map<String, String> = emptyMap()) {
        head(
            200,
            mapOf(
                "Content-Type" to "text/event-stream",
                "Transfer-Encoding" to "chunked",
                "Cache-Control" to "no-cache",
            ) + extraHeaders,
        )
    }

    fun chunk(bytes: ByteArray) {
        out.write((bytes.size.toString(16) + "\r\n").toByteArray(Charsets.ISO_8859_1))
        out.write(bytes)
        out.write("\r\n".toByteArray(Charsets.ISO_8859_1))
        out.flush()
    }

    fun chunk(text: String) {
        chunk(text.toByteArray(Charsets.UTF_8))
    }

    fun end() {
        out.write("0\r\n\r\n".toByteArray(Charsets.ISO_8859_1))
        out.flush()
    }

    fun fixedBody(status: Int, contentType: String, body: ByteArray, extraHeaders: Map<String, String> = emptyMap()) {
        head(
            status,
            mapOf("Content-Type" to contentType, "Content-Length" to body.size.toString()) + extraHeaders,
        )
        out.write(body)
        out.flush()
    }

    fun jsonError(status: Int, body: String, extraHeaders: Map<String, String> = emptyMap()) {
        fixedBody(status, "application/json", body.toByteArray(Charsets.UTF_8), extraHeaders)
    }

    /** 从客户端方向读一个字节：返回 true 表示对端已关闭（FIN 或 RST）。 */
    fun awaitClientClose(timeoutMs: Int): Boolean {
        socket.soTimeout = timeoutMs
        return try {
            socket.getInputStream().read() < 0
        } catch (e: IOException) {
            true
        }
    }

    fun resetConnection() {
        socket.setSoLinger(true, 0)
        runCatching { socket.close() }
    }

    fun close() {
        runCatching { socket.close() }
    }

    private fun reason(status: Int): String = when (status) {
        200 -> "OK"
        400 -> "Bad Request"
        401 -> "Unauthorized"
        403 -> "Forbidden"
        404 -> "Not Found"
        429 -> "Too Many Requests"
        500 -> "Internal Server Error"
        else -> "Status"
    }
}

internal class RawHttpServer(
    private val handler: (RawRequest, RawSink) -> Unit,
) : AutoCloseable {

    private val server: ServerSocket = ServerSocket(0, 50, InetAddress.getLoopbackAddress())
    private val failure: AtomicReference<Throwable?> = AtomicReference(null)

    val port: Int = server.localPort

    val url: String = "http://127.0.0.1:$port/v1/chat/completions"

    private val worker: Thread = Thread({ serve() }, "raw-http-server").apply {
        isDaemon = true
        start()
    }

    fun failureOrNull(): Throwable? = failure.get()

    override fun close() {
        runCatching { server.close() }
        worker.interrupt()
    }

    private fun serve() {
        while (!server.isClosed) {
            val socket = try {
                server.accept()
            } catch (e: IOException) {
                return
            }
            try {
                socket.use { handle(it) }
            } catch (e: Throwable) {
                failure.compareAndSet(null, e)
            }
        }
    }

    private fun handle(socket: Socket) {
        val input = BufferedInputStream(socket.getInputStream())
        val head = readHead(input)
        val lines = head.split("\r\n")
        val requestLine = lines.firstOrNull() ?: ""
        val headers = lines.drop(1).mapNotNull { line ->
            val colon = line.indexOf(':')
            if (colon <= 0) null else line.substring(0, colon).trim() to line.substring(colon + 1).trim()
        }.toMap()
        val length = headers.entries.firstOrNull { it.key.equals("content-length", true) }?.value?.toIntOrNull() ?: 0
        val body = ByteArrayOutputStream()
        var remaining = length
        while (remaining > 0) {
            val buffer = ByteArray(minOf(remaining, 8192))
            val read = input.read(buffer)
            if (read < 0) break
            body.write(buffer, 0, read)
            remaining -= read
        }
        handler(RawRequest(requestLine, headers, body.toByteArray()), RawSink(socket))
    }

    private fun readHead(input: InputStream): String {
        val sb = StringBuilder()
        var previous = 0
        while (true) {
            val b = input.read()
            if (b < 0) break
            sb.append(b.toChar())
            if (b == 0x0A && previous == 0x0D && sb.length >= 4 && sb.endsWith("\r\n\r\n")) break
            previous = b
        }
        return sb.toString()
    }
}
