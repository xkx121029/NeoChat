package com.neochat.app.chat

/**
 * provider 配置的落盘端口。实现放在 `data` 层（`SharedPreferences` + Keystore 封装），
 * 界面层只看见这个端口，因此测试可以用内存实现而不碰 Android 的存储。
 */
public interface ProviderSettings {

    /** 磁盘上的配置；密钥解不回来时 `apiKey` 为空，调用方按"未配置模型"处理。 */
    public fun load(): ProviderConfig

    /** 同步写盘；密钥封装失败返回 false（内存里的配置照常生效，仅下次冷启动会缺）。 */
    public fun save(config: ProviderConfig): Boolean
}
