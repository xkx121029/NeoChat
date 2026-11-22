package com.neochat.app.chat

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue

/**
 * 一个 OpenAI 兼容端点的连接参数。
 *
 * 密钥只留在进程内存里：本里程碑没有密钥库/加密层，落盘会把明文密钥留在设备上（见交付"待决问题"）。
 */
public data class ProviderConfig(
    public val baseUrl: String = "",
    public val apiKey: String = "",
    public val model: String = "",
) {
    public fun isReady(): Boolean = baseUrl.isNotBlank() && apiKey.isNotBlank() && model.isNotBlank()
}

/** 进程内单例式的配置持有者；`config` 是可观察状态，界面改完立即生效。 */
public class ProviderConfigStore(initial: ProviderConfig = ProviderConfig()) {

    public var config: ProviderConfig by mutableStateOf(initial)
        private set

    public val isReady: Boolean
        get() = config.isReady()

    public fun update(next: ProviderConfig) {
        config = ProviderConfig(
            baseUrl = next.baseUrl.trim(),
            apiKey = next.apiKey.trim(),
            model = next.model.trim(),
        )
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
