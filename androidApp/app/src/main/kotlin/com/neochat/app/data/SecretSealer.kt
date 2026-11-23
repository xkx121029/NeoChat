package com.neochat.app.data

import android.os.Build
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/**
 * 密钥静态存储（`docs/decisions/ADR-002-secrets-and-data-security.md` §2.1）：明文只在内存里出现，
 * 落盘的是平台密钥库封装后的密文。
 *
 * 封装格式固定为 `1 字节版本(0x01) + 12 字节 IV + 密文 + 16 字节 GCM 认证标签`；
 * 认证标签由 JCE 追加在密文尾部，因此解封时只读版本与 IV 两个头部字段。
 */
public interface SecretSealer {

    public fun seal(plain: ByteArray): ByteArray

    /** 解不开时抛异常：密钥条目不在、密文被改、或密文是从别的设备复制来的。 */
    public fun open(sealed: ByteArray): ByteArray
}

/**
 * Android Keystore 里的 AES-256-GCM 密钥（StrongBox 优先，不可用回退 TEE / 软件实现）。
 *
 * 密钥本体不离开 Keystore，进程只拿到句柄；密文没有可导出的密钥，
 * 所以换设备或清掉应用数据后必然解不开——这正是 ADR-002 §2.2 把跨端密钥迁移单独走口令导出的原因。
 */
public class KeystoreSecretSealer(private val alias: String) : SecretSealer {

    override fun seal(plain: ByteArray): ByteArray {
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, key())
        val iv = cipher.iv
        check(iv.size == IV_BYTES) { "GCM IV 长度异常：${iv.size}" }
        val body = cipher.doFinal(plain)
        val sealed = ByteArray(HEADER_BYTES + iv.size + body.size)
        sealed[0] = VERSION
        iv.copyInto(sealed, HEADER_BYTES)
        body.copyInto(sealed, HEADER_BYTES + iv.size)
        return sealed
    }

    override fun open(sealed: ByteArray): ByteArray {
        require(sealed.size > HEADER_BYTES + IV_BYTES) { "密文长度不足" }
        require(sealed[0] == VERSION) { "不认识的密文版本：${sealed[0]}" }
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(TAG_BITS, sealed, HEADER_BYTES, IV_BYTES))
        return cipher.doFinal(sealed, HEADER_BYTES + IV_BYTES, sealed.size - HEADER_BYTES - IV_BYTES)
    }

    private fun key(): SecretKey {
        val store = KeyStore.getInstance(PROVIDER)
        store.load(null)
        (store.getEntry(alias, null) as? KeyStore.SecretKeyEntry)?.let { return it.secretKey }
        return generate(store)
    }

    /**
     * 首次使用时生成并落进 Keystore。为了让属性可审计，密钥带 purpose 与 GCM padding 约束，
     * 不允许导出（AndroidKeyStore 的默认行为）。
     */
    private fun generate(store: KeyStore): SecretKey {
        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, PROVIDER)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            val strongBox = runCatching {
                generator.init(spec(strongBox = true))
                generator.generateKey()
            }.getOrNull()
            if (strongBox != null) return strongBox
            runCatching { store.deleteEntry(alias) }
        }
        generator.init(spec(strongBox = false))
        return generator.generateKey()
    }

    private fun spec(strongBox: Boolean): KeyGenParameterSpec {
        val builder = KeyGenParameterSpec.Builder(
            alias,
            KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT,
        )
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .setKeySize(KEY_BITS)
        if (strongBox && Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) builder.setIsStrongBoxBacked(true)
        return builder.build()
    }

    private companion object {
        const val PROVIDER = "AndroidKeyStore"
        const val TRANSFORMATION = "AES/GCM/NoPadding"
        const val KEY_BITS = 256
        const val IV_BYTES = 12
        const val TAG_BITS = 128
        const val HEADER_BYTES = 1
        const val VERSION: Byte = 1
    }
}

/** 密文的文本形态：Keystore 密文按 Base64(NO_WRAP) 存进 `SharedPreferences`。 */
public object SealedText {

    public fun encode(sealed: ByteArray): String = Base64.encodeToString(sealed, Base64.NO_WRAP)

    public fun decode(text: String): ByteArray = Base64.decode(text, Base64.NO_WRAP)
}
