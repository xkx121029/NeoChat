package com.neochat.app.data

import java.security.SecureRandom

/**
 * DDL 约定的主键形态：UUIDv7，36 字符小写，前 48 位是毫秒时间戳——按 id 排序即按时间排序。
 * 同一毫秒内用 74 位随机数区分，不做单调计数器（单设备不会产生可见乱序）。
 */
internal object Uuid7 {

    private val random: SecureRandom = SecureRandom()

    fun next(millis: Long): String {
        val bytes = ByteArray(16)
        random.nextBytes(bytes)
        bytes[0] = (millis ushr 40).toByte()
        bytes[1] = (millis ushr 32).toByte()
        bytes[2] = (millis ushr 24).toByte()
        bytes[3] = (millis ushr 16).toByte()
        bytes[4] = (millis ushr 8).toByte()
        bytes[5] = millis.toByte()
        bytes[6] = (bytes[6].toInt() and 0x0F or 0x70).toByte()
        bytes[8] = (bytes[8].toInt() and 0x3F or 0x80).toByte()
        val hex = StringBuilder(36)
        for (index in bytes.indices) {
            if (index == 4 || index == 6 || index == 8 || index == 10) hex.append('-')
            hex.append(HEX[(bytes[index].toInt() shr 4) and 0x0F])
            hex.append(HEX[bytes[index].toInt() and 0x0F])
        }
        return hex.toString()
    }

    /** 每行唯一的修订串（`rev` 列），与 `sync_version` 一起为将来同步留位。 */
    fun revision(): String {
        val bytes = ByteArray(16)
        random.nextBytes(bytes)
        val hex = StringBuilder(32)
        for (byte in bytes) {
            hex.append(HEX[(byte.toInt() shr 4) and 0x0F])
            hex.append(HEX[byte.toInt() and 0x0F])
        }
        return hex.toString()
    }

    private const val HEX = "0123456789abcdef"
}
