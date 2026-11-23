package com.neochat.app.data

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import com.neochat.app.chat.ProviderConfig
import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

/**
 * 配置落盘的单测：冷启动读回、密钥不以明文出现、密文损坏/密钥缺失时降级为"未配置模型"。
 *
 * 这里用 `ReverseSealer` 替身（字节反转）而不是真 Keystore：Robolectric 没有 AndroidKeyStore 服务，
 * 真实现（AES-256-GCM + StrongBox 回退）在 `androidTest/KeystoreSecretSealerTest` 里跑真机。
 */
@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class ProviderSettingsTest {

    private val context = ApplicationProvider.getApplicationContext<Context>()

    private val config = ProviderConfig(
        baseUrl = "https://api.example.com/v1",
        apiKey = "demo-key-abcdef1234567890",
        model = "deepseek-chat",
    )

    @Test
    fun `保存后新实例能读回，等于冷启动恢复`() {
        assertTrue(settings().save(config))

        val restored = settings().load()

        assertEquals(config, restored)
        assertTrue(restored.isReady())
    }

    @Test
    fun `明文密钥不落盘，provider 元数据与密钥句柄都写了`() {
        assertTrue(settings().save(config))

        val raw = prefsFile().readText(Charsets.UTF_8)

        assertFalse(raw.contains(config.apiKey))
        assertTrue(raw.contains("provider.id"))
        assertTrue(raw.contains("provider.name"))
        assertTrue(raw.contains("provider.protocol"))
        assertTrue(raw.contains("provider.base_url"))
        assertTrue(raw.contains("provider.model"))
        assertTrue(raw.contains("provider.key_ref"))
        assertTrue(raw.contains("provider_key_default"))
    }

    @Test
    fun `密文损坏时只丢密钥，base_url 与 model 保留，isReady 为 false`() {
        settings().save(config)
        context.getSharedPreferences(FILE, Context.MODE_PRIVATE).edit()
            .putString("provider.key_sealed", "AAAA")
            .commit()

        val restored = settings().load()

        assertEquals("", restored.apiKey)
        assertEquals(config.baseUrl, restored.baseUrl)
        assertEquals(config.model, restored.model)
        assertFalse(restored.isReady())
    }

    @Test
    fun `密钥条目消失（换设备或清数据）时降级为未配置模型，不抛异常`() {
        settings().save(config)

        val restored = settings(ForeignSealer()).load()

        assertEquals("", restored.apiKey)
        assertFalse(restored.isReady())
    }

    @Test
    fun `从未保存过时读回全空配置`() {
        val restored = settings().load()

        assertEquals(ProviderConfig(), restored)
        assertFalse(restored.isReady())
    }

    @Test
    fun `清空密钥后不再保留密文与提示`() {
        settings().save(config)
        settings().save(config.copy(apiKey = ""))

        val raw = prefsFile().readText(Charsets.UTF_8)

        assertFalse(raw.contains("provider.key_sealed"))
        assertFalse(raw.contains("provider.key_hint_head"))
        assertEquals("", settings().load().apiKey)
    }

    private fun settings(sealer: SecretSealer = ReverseSealer()) =
        SharedPreferencesProviderSettings(context, sealer)

    private fun prefsFile(): File = File(context.dataDir, "shared_prefs/$FILE.xml")

    /** 单测替身：字节反转，只验证"密文不是明文"与格式/降级路径。 */
    private class ReverseSealer : SecretSealer {
        override fun seal(plain: ByteArray): ByteArray = byteArrayOf(7) + plain.reversedArray()

        override fun open(sealed: ByteArray): ByteArray {
            require(sealed.size > 1 && sealed[0] == 7.toByte()) { "not sealed by ReverseSealer" }
            return sealed.copyOfRange(1, sealed.size).reversedArray()
        }
    }

    /** 模拟"Keystore 里没有这把钥匙"：封装/解封都失败。 */
    private class ForeignSealer : SecretSealer {
        override fun seal(plain: ByteArray): ByteArray = plain

        override fun open(sealed: ByteArray): ByteArray = error("no key entry")
    }

    private companion object {
        const val FILE = "provider_settings"
    }
}
