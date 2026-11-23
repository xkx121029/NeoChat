package com.neochat.app.data

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.neochat.app.chat.ProviderConfig
import java.security.KeyStore
import org.junit.After
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

/**
 * 真机验证 Keystore 封装：密文格式、随机 IV、认证标签、alias 复用，以及"没有那把钥匙就解不开"。
 * Robolectric 没有 AndroidKeyStore 服务，所以这条只能跑在设备上。
 */
@RunWith(AndroidJUnit4::class)
class KeystoreSecretSealerTest {

    private val alias = "neochat_test_key_" + System.nanoTime()

    @After
    fun tearDown() {
        runCatching {
            KeyStore.getInstance("AndroidKeyStore").apply { load(null) }.deleteEntry(alias)
        }
    }

    @Test
    fun `封装后能解回原文_格式是版本字节加IV加密文与标签`() {
        val sealer = KeystoreSecretSealer(alias)
        val plain = "demo-key-0123456789abcdef".toByteArray()

        val sealed = sealer.seal(plain)

        assertEquals(1, sealed[0].toInt())
        assertTrue("密文长度 = 版本 + IV + 标签 + 正文: ${sealed.size}", sealed.size >= 1 + 12 + 16 + plain.size)
        assertFalse(String(sealed, Charsets.ISO_8859_1).contains("demo-key"))
        assertArrayEquals(plain, sealer.open(sealed))
    }

    @Test
    fun `同一明文两次封装结果不同_但都能解回`() {
        val sealer = KeystoreSecretSealer(alias)
        val plain = "same-plaintext".toByteArray()

        val first = sealer.seal(plain)
        val second = sealer.seal(plain)

        assertFalse(first.contentEquals(second))
        assertArrayEquals(plain, sealer.open(first))
        assertArrayEquals(plain, sealer.open(second))
        assertArrayEquals(plain, KeystoreSecretSealer(alias).open(first))
    }

    @Test
    fun `密文被改一个字节就解不开`() {
        val sealer = KeystoreSecretSealer(alias)
        val sealed = sealer.seal("tamper-me".toByteArray())
        sealed[sealed.size - 1] = (sealed[sealed.size - 1].toInt() xor 0x01).toByte()

        val failure = runCatching { sealer.open(sealed) }

        assertTrue("GCM 认证标签必须拒绝被改的密文", failure.isFailure)
    }

    @Test
    fun `换一把钥匙_别的alias_解不开同一份密文`() {
        val sealed = KeystoreSecretSealer(alias).seal("cross-device".toByteArray())
        val other = KeystoreSecretSealer(alias + "_other")

        val failure = runCatching { other.open(sealed) }

        assertTrue("没有那把钥匙就必须失败", failure.isFailure)
    }

    @Test
    fun `配置通过真Keystore落盘后能在新实例里读回`() {
        val context = ApplicationProvider.getApplicationContext<Context>()
        val config = ProviderConfig("https://api.example.com/v1", "demo-key-9876543210", "deepseek-chat")

        val saved = SharedPreferencesProviderSettings(context, KeystoreSecretSealer(alias), alias).save(config)
        val restored = SharedPreferencesProviderSettings(context, KeystoreSecretSealer(alias), alias).load()

        assertTrue(saved)
        assertEquals(config, restored)
    }
}
