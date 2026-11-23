package com.neochat.app.chat

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue

/**
 * 一个 OpenAI 兼容端点的连接参数。
 *
 * 落盘口径见 [ProviderSettings]：base_url / model 明文可读，密钥经 Android Keystore 封装后
 * 才写进 `SharedPreferences`（ADR-002 §2.1），冷启动从这里读回并自动选中。
 */
public data class ProviderConfig(
    public val baseUrl: String = "",
    public val apiKey: String = "",
    public val model: String = "",
) {
    public fun isReady(): Boolean = baseUrl.isNotBlank() && apiKey.isNotBlank() && model.isNotBlank()
}

/** 进程内单例式的配置持有者；`config` 是可观察状态，界面改完立即生效并顺手落盘。 */
public class ProviderConfigStore(
    initial: ProviderConfig = ProviderConfig(),
    private val settings: ProviderSettings? = null,
) {

    public var config: ProviderConfig by mutableStateOf(initial)
        private set

    public val isReady: Boolean
        get() = config.isReady()

    /** 先更新内存再落盘；返回 false 表示密钥没能封装写盘（界面据此提示，不抛给用户）。 */
    public fun update(next: ProviderConfig): Boolean {
        config = ProviderConfig(
            baseUrl = next.baseUrl.trim(),
            apiKey = next.apiKey.trim(),
            model = next.model.trim(),
        )
        return settings?.save(config) ?: true
    }

    /** 错误里的 `edit_provider {focus:"key"|"base_url"}` 用它决定对话框里先聚焦哪个字段。 */
    public fun focusField(actionParams: Map<String, String>): ProviderField {
        return when (actionParams["focus"]) {
            FOCUS_KEY -> ProviderField.API_KEY
            FOCUS_BASE_URL -> ProviderField.BASE_URL
            else -> if (config.apiKey.isBlank()) ProviderField.API_KEY else ProviderField.BASE_URL
        }
    }

    public companion object {
        /** providers.md §1 的协议 id。 */
        public const val PROVIDER_ID: String = "openai_compatible"

        public const val FOCUS_KEY: String = "key"
        public const val FOCUS_BASE_URL: String = "base_url"
    }
}

/** 配置对话框里的字段。 */
public enum class ProviderField {
    BASE_URL,
    API_KEY,
    MODEL,
}
