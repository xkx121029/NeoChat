package com.neochat.app.data

import android.content.Context
import android.util.Log
import com.neochat.app.chat.ProviderConfig
import com.neochat.app.chat.ProviderSettings

/**
 * provider 配置落盘。模块里没有 DataStore 依赖，按任务口径直接用 `SharedPreferences`，不引新库。
 *
 * 落盘的是五类事实：`provider.id` / `name` / `protocol` / `base_url` / `model` 加上 `key_ref`
 * （密钥库条目名，不含密钥内容）；密钥本体经 [SecretSealer] 封装成 `key_sealed`，
 * 明文展示用途只留首尾各 4 位（ADR-002 §3）。
 */
public class SharedPreferencesProviderSettings(
    context: Context,
    private val sealer: SecretSealer,
    private val keyAlias: String = DEFAULT_KEY_ALIAS,
) : ProviderSettings {

    private val prefs = context.getSharedPreferences(FILE, Context.MODE_PRIVATE)

    /**
     * 缺字段、密钥条目没了、密文被改过，一律降级成"这段字段为空"而不是抛异常：
     * 上层看到的是 `ProviderConfig.isReady() == false`，界面显示"未配置模型"。
     */
    override fun load(): ProviderConfig {
        val sealed = prefs.getString(KEY_SEALED, null)
        val apiKey = if (sealed.isNullOrEmpty()) {
            ""
        } else {
            runCatching { String(sealer.open(SealedText.decode(sealed)), Charsets.UTF_8) }
                .getOrElse { failure ->
                    Log.w(TAG, "provider key unreadable (${failure.javaClass.simpleName}); degraded to unconfigured")
                    ""
                }
        }
        val config = ProviderConfig(
            baseUrl = prefs.getString(KEY_BASE_URL, "").orEmpty(),
            apiKey = apiKey,
            model = prefs.getString(KEY_MODEL, "").orEmpty(),
        )
        // 只记"有没有解出密钥"和模型名：base_url 与密钥都不进日志（ADR-002 §3）。
        val keyState = if (apiKey.isEmpty()) "absent" else "keystore"
        Log.i(TAG, "provider restored (model=${config.model.ifBlank { "unset" }}, key=$keyState)")
        return config
    }

    override fun save(config: ProviderConfig): Boolean {
        val editor = prefs.edit()
            .putString(KEY_ID, PROVIDER_ID)
            .putString(KEY_NAME, PROVIDER_NAME)
            .putString(KEY_PROTOCOL, PROVIDER_PROTOCOL)
            .putString(KEY_BASE_URL, config.baseUrl)
            .putString(KEY_MODEL, config.model)
            .putString(KEY_KEY_REF, keyAlias)
        if (config.apiKey.isEmpty()) {
            editor.remove(KEY_SEALED).remove(KEY_HINT_HEAD).remove(KEY_HINT_TAIL)
        } else {
            val sealed = runCatching { SealedText.encode(sealer.seal(config.apiKey.toByteArray())) }
                .getOrElse { failure ->
                    Log.w(TAG, "provider key seal failed (${failure.javaClass.simpleName})")
                    return false
                }
            editor
                .putString(KEY_SEALED, sealed)
                .putString(KEY_HINT_HEAD, config.apiKey.take(HINT_CHARS))
                .putString(KEY_HINT_TAIL, config.apiKey.takeLast(HINT_CHARS))
        }
        // 同步写盘：用户在对话框点保存后进程随时可能被杀，apply() 的后台队列不保证那一刻已落地。
        return editor.commit()
    }

    private companion object {
        const val TAG = "NeoChatSettings"
        const val FILE = "provider_settings"
        const val PROVIDER_ID = "openai_compatible"
        const val PROVIDER_NAME = "OpenAI 兼容"
        const val PROVIDER_PROTOCOL = "openai_compatible"
        const val DEFAULT_KEY_ALIAS = "provider_key_default"
        const val HINT_CHARS = 4
        const val KEY_ID = "provider.id"
        const val KEY_NAME = "provider.name"
        const val KEY_PROTOCOL = "provider.protocol"
        const val KEY_BASE_URL = "provider.base_url"
        const val KEY_MODEL = "provider.model"
        const val KEY_KEY_REF = "provider.key_ref"
        const val KEY_SEALED = "provider.key_sealed"
        const val KEY_HINT_HEAD = "provider.key_hint_head"
        const val KEY_HINT_TAIL = "provider.key_hint_tail"
    }
}
