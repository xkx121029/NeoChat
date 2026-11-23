package com.neochat.app.data

/**
 * `sync.device_id`：建库后第一次取时生成并落进 `app_setting`，之后一直在进程内缓存。
 * 单独成类只是为了让 [RoomChatStore] 的职责停在"会话与消息"上。
 */
internal class DeviceIdentity(
    private val db: NeoChatDatabase,
    private val now: () -> Long,
) {

    private var cached: String? = null

    suspend fun get(): String {
        cached?.let { return it }
        val stored = db.settings().value(KEY)
        val id = stored?.trim('"') ?: Uuid7.next(now())
        if (stored == null) {
            db.settings().put(AppSettingEntity(key = KEY, valueJson = "\"$id\"", updatedAt = now()))
        }
        cached = id
        return id
    }

    private companion object {
        const val KEY = "sync.device_id"
    }
}
