package com.neochat.app.data

import androidx.room.ColumnInfo
import androidx.room.Entity
import androidx.room.ForeignKey
import androidx.room.Index
import androidx.room.PrimaryKey
import androidx.room.RoomWarnings

/**
 * `spec/schema/v1.sql` 二十二张实体表的 Room 映射，列名、类型、可空、默认值与 DDL 逐列对应。
 *
 * 三类 DDL 约束 Room 表达不了，见交付报告的差异清单：CHECK 值域、部分索引的 WHERE、FTS5 虚表
 * （虚表由 [NeoChatDatabase] 的建库回调原样执行）。
 */

/** DDL `agent_profile`。 */
@Entity(
    tableName = "agent_profile",
    foreignKeys = [
        ForeignKey(
            entity = ModelEntity::class,
            parentColumns = ["id"],
            childColumns = ["default_model_id"],
            onDelete = ForeignKey.SET_NULL,
        ),
    ],
)
@SuppressWarnings(RoomWarnings.MISSING_INDEX_ON_FOREIGN_KEY_CHILD)
internal data class AgentProfileEntity(
    @PrimaryKey val id: String,
    val name: String,
    @ColumnInfo(name = "avatar_ref") val avatarRef: String?,
    @ColumnInfo(name = "system_prompt", defaultValue = "''") val systemPrompt: String,
    @ColumnInfo(name = "default_model_id") val defaultModelId: String?,
    @ColumnInfo(name = "sampling_json") val samplingJson: String?,
    @ColumnInfo(name = "tool_allow_json") val toolAllowJson: String?,
    @ColumnInfo(name = "mcp_server_ids_json") val mcpServerIdsJson: String?,
    val greeting: String?,
    @ColumnInfo(name = "context_policy_json") val contextPolicyJson: String?,
    @ColumnInfo(name = "memory_scope", defaultValue = "'agent'") val memoryScope: String,
    @ColumnInfo(name = "sort_order", defaultValue = "0") val sortOrder: Long,
    @ColumnInfo(name = "sync_version", defaultValue = "1") val syncVersion: Long,
    val rev: String,
    @ColumnInfo(name = "origin_device") val originDevice: String?,
    @ColumnInfo(name = "created_at") val createdAt: Long,
    @ColumnInfo(name = "updated_at") val updatedAt: Long,
    @ColumnInfo(name = "deleted_at") val deletedAt: Long?
)

/** DDL `app_setting`。 */
@Entity(
    tableName = "app_setting",
)
internal data class AppSettingEntity(
    @PrimaryKey val key: String,
    @ColumnInfo(name = "value_json") val valueJson: String,
    @ColumnInfo(name = "updated_at") val updatedAt: Long
)

/** DDL `attachment`。 */
@Entity(
    tableName = "attachment",
)
internal data class AttachmentEntity(
    @PrimaryKey val id: String,
    val sha256: String,
    @ColumnInfo(name = "size_bytes") val sizeBytes: Long,
    val mime: String,
    val kind: String,
    @ColumnInfo(name = "local_path") val localPath: String,
    @ColumnInfo(name = "page_count") val pageCount: Long?,
    @ColumnInfo(name = "extracted_text") val extractedText: String?,
    @ColumnInfo(name = "ref_count", defaultValue = "0") val refCount: Long,
    @ColumnInfo(name = "created_at") val createdAt: Long,
    @ColumnInfo(name = "deleted_at") val deletedAt: Long?
)

/** DDL `conversation`。 */
@Entity(
    tableName = "conversation",
    foreignKeys = [
        ForeignKey(
            entity = AgentProfileEntity::class,
            parentColumns = ["id"],
            childColumns = ["agent_id"],
            onDelete = ForeignKey.SET_NULL,
        ),
        ForeignKey(
            entity = FolderEntity::class,
            parentColumns = ["id"],
            childColumns = ["folder_id"],
            onDelete = ForeignKey.SET_NULL,
        ),
    ],
    indices = [
        Index(value = ["archived", "pinned", "last_message_at"], name = "idx_conv_list"),
        Index(value = ["folder_id"], name = "idx_conv_folder"),
    ],
)
@SuppressWarnings(RoomWarnings.MISSING_INDEX_ON_FOREIGN_KEY_CHILD)
internal data class ConversationEntity(
    @PrimaryKey val id: String,
    @ColumnInfo(defaultValue = "''") val title: String,
    @ColumnInfo(name = "title_auto", defaultValue = "1") val titleAuto: Boolean,
    @ColumnInfo(name = "agent_id") val agentId: String?,
    @ColumnInfo(name = "folder_id") val folderId: String?,
    @ColumnInfo(defaultValue = "0") val pinned: Boolean,
    @ColumnInfo(defaultValue = "0") val archived: Boolean,
    @ColumnInfo(name = "active_leaf_message_id") val activeLeafMessageId: String?,
    @ColumnInfo(name = "settings_json") val settingsJson: String?,
    @ColumnInfo(name = "last_message_at") val lastMessageAt: Long?,
    @ColumnInfo(name = "message_count", defaultValue = "0") val messageCount: Long,
    @ColumnInfo(name = "sync_version", defaultValue = "1") val syncVersion: Long,
    val rev: String,
    @ColumnInfo(name = "origin_device") val originDevice: String?,
    @ColumnInfo(name = "created_at") val createdAt: Long,
    @ColumnInfo(name = "updated_at") val updatedAt: Long,
    @ColumnInfo(name = "deleted_at") val deletedAt: Long?
)

/** DDL `conversation_tag`。 */
@Entity(
    tableName = "conversation_tag",
    foreignKeys = [
        ForeignKey(
            entity = ConversationEntity::class,
            parentColumns = ["id"],
            childColumns = ["conversation_id"],
            onDelete = ForeignKey.CASCADE,
        ),
        ForeignKey(
            entity = TagEntity::class,
            parentColumns = ["id"],
            childColumns = ["tag_id"],
            onDelete = ForeignKey.CASCADE,
        ),
    ],
    primaryKeys = ["conversation_id", "tag_id"],
)
@SuppressWarnings(RoomWarnings.MISSING_INDEX_ON_FOREIGN_KEY_CHILD)
internal data class ConversationTagEntity(
    @ColumnInfo(name = "conversation_id") val conversationId: String,
    @ColumnInfo(name = "tag_id") val tagId: String
)

/** DDL `folder`。 */
@Entity(
    tableName = "folder",
    foreignKeys = [
        ForeignKey(
            entity = FolderEntity::class,
            parentColumns = ["id"],
            childColumns = ["parent_id"],
            onDelete = ForeignKey.CASCADE,
        ),
    ],
)
@SuppressWarnings(RoomWarnings.MISSING_INDEX_ON_FOREIGN_KEY_CHILD)
internal data class FolderEntity(
    @PrimaryKey val id: String,
    val name: String,
    @ColumnInfo(name = "parent_id") val parentId: String?,
    @ColumnInfo(name = "sort_order", defaultValue = "0") val sortOrder: Long,
    @ColumnInfo(name = "created_at") val createdAt: Long,
    @ColumnInfo(name = "updated_at") val updatedAt: Long,
    @ColumnInfo(name = "deleted_at") val deletedAt: Long?
)

/** DDL `mcp_server`。 */
@Entity(
    tableName = "mcp_server",
)
internal data class McpServerEntity(
    @PrimaryKey val id: String,
    val name: String,
    val transport: String,
    val url: String?,
    val command: String?,
    @ColumnInfo(name = "args_json") val argsJson: String?,
    @ColumnInfo(name = "env_json") val envJson: String?,
    @ColumnInfo(name = "headers_json") val headersJson: String?,
    @ColumnInfo(defaultValue = "1") val enabled: Boolean,
    @ColumnInfo(name = "last_check_at") val lastCheckAt: Long?,
    @ColumnInfo(name = "last_check_ok") val lastCheckOk: Boolean?,
    @ColumnInfo(name = "created_at") val createdAt: Long,
    @ColumnInfo(name = "updated_at") val updatedAt: Long,
    @ColumnInfo(name = "deleted_at") val deletedAt: Long?
)

/** DDL `mcp_tool`。 */
@Entity(
    tableName = "mcp_tool",
    foreignKeys = [
        ForeignKey(
            entity = McpServerEntity::class,
            parentColumns = ["id"],
            childColumns = ["server_id"],
            onDelete = ForeignKey.CASCADE,
        ),
    ],
    indices = [
        Index(value = ["server_id", "name"], name = "idx_mcp_tool_server_id_name", unique = true),
    ],
)
@SuppressWarnings(RoomWarnings.MISSING_INDEX_ON_FOREIGN_KEY_CHILD)
internal data class McpToolEntity(
    @PrimaryKey val id: String,
    @ColumnInfo(name = "server_id") val serverId: String,
    val name: String,
    val description: String?,
    @ColumnInfo(name = "input_schema_json") val inputSchemaJson: String?,
    @ColumnInfo(defaultValue = "1") val enabled: Boolean,
    @ColumnInfo(name = "danger_level", defaultValue = "'safe'") val dangerLevel: String,
    @ColumnInfo(name = "created_at") val createdAt: Long
)

/** DDL `memory_item`。 */
@Entity(
    tableName = "memory_item",
    foreignKeys = [
        ForeignKey(
            entity = AgentProfileEntity::class,
            parentColumns = ["id"],
            childColumns = ["agent_id"],
            onDelete = ForeignKey.CASCADE,
        ),
    ],
    indices = [
        Index(value = ["scope", "agent_id"], name = "idx_memory_scope"),
    ],
)
@SuppressWarnings(RoomWarnings.MISSING_INDEX_ON_FOREIGN_KEY_CHILD)
internal data class MemoryItemEntity(
    @PrimaryKey val id: String,
    val scope: String,
    @ColumnInfo(name = "agent_id") val agentId: String?,
    val content: String,
    val keywords: String?,
    @ColumnInfo(defaultValue = "0") val pinned: Boolean,
    @ColumnInfo(name = "source_conversation_id") val sourceConversationId: String?,
    @ColumnInfo(name = "source_message_id") val sourceMessageId: String?,
    @ColumnInfo(name = "sync_version", defaultValue = "1") val syncVersion: Long,
    val rev: String,
    @ColumnInfo(name = "origin_device") val originDevice: String?,
    @ColumnInfo(name = "created_at") val createdAt: Long,
    @ColumnInfo(name = "updated_at") val updatedAt: Long,
    @ColumnInfo(name = "deleted_at") val deletedAt: Long?
)

/** DDL `message`。 */
@Entity(
    tableName = "message",
    foreignKeys = [
        ForeignKey(
            entity = ConversationEntity::class,
            parentColumns = ["id"],
            childColumns = ["conversation_id"],
            onDelete = ForeignKey.CASCADE,
        ),
        ForeignKey(
            entity = MessageEntity::class,
            parentColumns = ["id"],
            childColumns = ["parent_id"],
            onDelete = ForeignKey.CASCADE,
        ),
    ],
    indices = [
        Index(value = ["conversation_id", "created_at"], name = "idx_msg_recent"),
    ],
)
@SuppressWarnings(RoomWarnings.MISSING_INDEX_ON_FOREIGN_KEY_CHILD)
internal data class MessageEntity(
    @PrimaryKey val id: String,
    @ColumnInfo(name = "conversation_id") val conversationId: String,
    @ColumnInfo(name = "parent_id") val parentId: String?,
    val role: String,
    @ColumnInfo(name = "sibling_index", defaultValue = "0") val siblingIndex: Long,
    @ColumnInfo(defaultValue = "'complete'") val status: String,
    @ColumnInfo(name = "finish_reason") val finishReason: String?,
    @ColumnInfo(name = "error_json") val errorJson: String?,
    @ColumnInfo(name = "provider_id") val providerId: String?,
    @ColumnInfo(name = "model_id") val modelId: String?,
    @ColumnInfo(defaultValue = "0") val truncated: Boolean,
    @ColumnInfo(name = "sync_version", defaultValue = "1") val syncVersion: Long,
    val rev: String,
    @ColumnInfo(name = "origin_device") val originDevice: String?,
    @ColumnInfo(name = "created_at") val createdAt: Long,
    @ColumnInfo(name = "updated_at") val updatedAt: Long,
    @ColumnInfo(name = "deleted_at") val deletedAt: Long?
)

/** DDL `message_attachment`。 */
@Entity(
    tableName = "message_attachment",
    foreignKeys = [
        ForeignKey(
            entity = MessageEntity::class,
            parentColumns = ["id"],
            childColumns = ["message_id"],
            onDelete = ForeignKey.CASCADE,
        ),
        ForeignKey(
            entity = AttachmentEntity::class,
            parentColumns = ["id"],
            childColumns = ["attachment_id"],
            onDelete = ForeignKey.CASCADE,
        ),
    ],
    primaryKeys = ["message_id", "attachment_id", "part_index"],
)
@SuppressWarnings(RoomWarnings.MISSING_INDEX_ON_FOREIGN_KEY_CHILD)
internal data class MessageAttachmentEntity(
    @ColumnInfo(name = "message_id") val messageId: String,
    @ColumnInfo(name = "attachment_id") val attachmentId: String,
    @ColumnInfo(name = "part_index", defaultValue = "0") val partIndex: Long,
    @ColumnInfo(name = "role_hint") val roleHint: String?
)

/** DDL `message_part`。 */
@Entity(
    tableName = "message_part",
    foreignKeys = [
        ForeignKey(
            entity = MessageEntity::class,
            parentColumns = ["id"],
            childColumns = ["message_id"],
            onDelete = ForeignKey.CASCADE,
        ),
    ],
    indices = [
        Index(value = ["message_id", "part_index"], name = "idx_part_message", unique = true),
    ],
)
@SuppressWarnings(RoomWarnings.MISSING_INDEX_ON_FOREIGN_KEY_CHILD)
internal data class MessagePartEntity(
    @PrimaryKey val id: String,
    @ColumnInfo(name = "message_id") val messageId: String,
    @ColumnInfo(name = "part_index") val partIndex: Long,
    val kind: String,
    val body: String?,
    @ColumnInfo(name = "meta_json") val metaJson: String?,
    @ColumnInfo(defaultValue = "0") val truncated: Boolean,
    @ColumnInfo(name = "created_at") val createdAt: Long,
    @ColumnInfo(name = "updated_at") val updatedAt: Long
)

/** DDL `migration`。 */
@Entity(
    tableName = "migration",
)
internal data class MigrationEntity(
    @PrimaryKey(autoGenerate = true) val id: Long,
    val name: String,
    val checksum: String,
    @ColumnInfo(name = "applied_at") val appliedAt: Long,
    @ColumnInfo(name = "snapshot_path") val snapshotPath: String?,
    @ColumnInfo(name = "integrity_ok", defaultValue = "0") val integrityOk: Boolean
)

/** DDL `model`。 */
@Entity(
    tableName = "model",
    foreignKeys = [
        ForeignKey(
            entity = ProviderEntity::class,
            parentColumns = ["id"],
            childColumns = ["provider_id"],
            onDelete = ForeignKey.CASCADE,
        ),
    ],
    indices = [
        Index(value = ["provider_id"], name = "idx_model_provider"),
        Index(value = ["provider_id", "model_key"], name = "idx_model_provider_id_model_key", unique = true),
    ],
)
@SuppressWarnings(RoomWarnings.MISSING_INDEX_ON_FOREIGN_KEY_CHILD)
internal data class ModelEntity(
    @PrimaryKey val id: String,
    @ColumnInfo(name = "provider_id") val providerId: String,
    @ColumnInfo(name = "model_key") val modelKey: String,
    @ColumnInfo(name = "display_name") val displayName: String,
    @ColumnInfo(name = "context_window") val contextWindow: Long?,
    @ColumnInfo(name = "max_output") val maxOutput: Long?,
    @ColumnInfo(name = "supports_vision", defaultValue = "0") val supportsVision: Boolean,
    @ColumnInfo(name = "supports_tools", defaultValue = "0") val supportsTools: Boolean,
    @ColumnInfo(name = "supports_thinking", defaultValue = "0") val supportsThinking: Boolean,
    @ColumnInfo(name = "supports_cache", defaultValue = "0") val supportsCache: Boolean,
    @ColumnInfo(name = "supports_json_schema", defaultValue = "0") val supportsJsonSchema: Boolean,
    @ColumnInfo(name = "max_tokens_param", defaultValue = "'max_tokens'") val maxTokensParam: String,
    @ColumnInfo(name = "price_input") val priceInput: Double?,
    @ColumnInfo(name = "price_output") val priceOutput: Double?,
    @ColumnInfo(name = "price_cache_read") val priceCacheRead: Double?,
    @ColumnInfo(name = "price_cache_write") val priceCacheWrite: Double?,
    @ColumnInfo(defaultValue = "'USD'") val currency: String,
    @ColumnInfo(name = "thinking_budget_max") val thinkingBudgetMax: Long?,
    @ColumnInfo(name = "is_custom", defaultValue = "0") val isCustom: Boolean,
    @ColumnInfo(name = "pinned_by_user", defaultValue = "0") val pinnedByUser: Boolean,
    @ColumnInfo(name = "extra_json") val extraJson: String?,
    @ColumnInfo(name = "sync_version", defaultValue = "1") val syncVersion: Long,
    val rev: String,
    @ColumnInfo(name = "origin_device") val originDevice: String?,
    @ColumnInfo(name = "created_at") val createdAt: Long,
    @ColumnInfo(name = "updated_at") val updatedAt: Long,
    @ColumnInfo(name = "deleted_at") val deletedAt: Long?
)

/** DDL `prompt_template`。 */
@Entity(
    tableName = "prompt_template",
    indices = [
        Index(value = ["category"], name = "idx_prompt_category"),
    ],
)
internal data class PromptTemplateEntity(
    @PrimaryKey val id: String,
    val title: String,
    val category: String?,
    val body: String,
    @ColumnInfo(name = "variables_json") val variablesJson: String?,
    @ColumnInfo(name = "sort_order", defaultValue = "0") val sortOrder: Long,
    @ColumnInfo(name = "sync_version", defaultValue = "1") val syncVersion: Long,
    val rev: String,
    @ColumnInfo(name = "origin_device") val originDevice: String?,
    @ColumnInfo(name = "created_at") val createdAt: Long,
    @ColumnInfo(name = "updated_at") val updatedAt: Long,
    @ColumnInfo(name = "deleted_at") val deletedAt: Long?
)

/** DDL `provider`。 */
@Entity(
    tableName = "provider",
    indices = [
        Index(value = ["deleted_at"], name = "idx_provider_alive"),
    ],
)
internal data class ProviderEntity(
    @PrimaryKey val id: String,
    val name: String,
    val protocol: String,
    @ColumnInfo(name = "base_url") val baseUrl: String,
    @ColumnInfo(name = "org_id") val orgId: String?,
    @ColumnInfo(name = "proxy_mode", defaultValue = "'system'") val proxyMode: String,
    @ColumnInfo(name = "proxy_url") val proxyUrl: String?,
    @ColumnInfo(name = "connect_timeout_ms", defaultValue = "10000") val connectTimeoutMs: Long,
    @ColumnInfo(name = "read_idle_timeout_ms", defaultValue = "180000") val readIdleTimeoutMs: Long,
    @ColumnInfo(name = "first_byte_timeout_ms", defaultValue = "60000") val firstByteTimeoutMs: Long,
    @ColumnInfo(name = "total_timeout_ms", defaultValue = "0") val totalTimeoutMs: Long,
    @ColumnInfo(name = "max_retries", defaultValue = "2") val maxRetries: Long,
    @ColumnInfo(name = "concurrency_limit", defaultValue = "4") val concurrencyLimit: Long,
    @ColumnInfo(name = "extra_headers_json") val extraHeadersJson: String?,
    @ColumnInfo(name = "extra_query_json") val extraQueryJson: String?,
    @ColumnInfo(name = "extra_body_json") val extraBodyJson: String?,
    @ColumnInfo(name = "pass_through", defaultValue = "0") val passThrough: Boolean,
    @ColumnInfo(name = "cumulative_stream", defaultValue = "0") val cumulativeStream: Boolean,
    @ColumnInfo(name = "billing_url") val billingUrl: String?,
    @ColumnInfo(name = "status_url") val statusUrl: String?,
    @ColumnInfo(name = "sort_order", defaultValue = "0") val sortOrder: Long,
    @ColumnInfo(name = "sync_version", defaultValue = "1") val syncVersion: Long,
    val rev: String,
    @ColumnInfo(name = "origin_device") val originDevice: String?,
    @ColumnInfo(name = "created_at") val createdAt: Long,
    @ColumnInfo(name = "updated_at") val updatedAt: Long,
    @ColumnInfo(name = "deleted_at") val deletedAt: Long?
)

/** DDL `provider_key`。 */
@Entity(
    tableName = "provider_key",
    foreignKeys = [
        ForeignKey(
            entity = ProviderEntity::class,
            parentColumns = ["id"],
            childColumns = ["provider_id"],
            onDelete = ForeignKey.CASCADE,
        ),
    ],
    indices = [
        Index(value = ["provider_id", "enabled"], name = "idx_provider_key_lookup"),
        Index(value = ["provider_id", "key_ref"], name = "idx_provider_key_provider_id_key_ref", unique = true),
    ],
)
@SuppressWarnings(RoomWarnings.MISSING_INDEX_ON_FOREIGN_KEY_CHILD)
internal data class ProviderKeyEntity(
    @PrimaryKey val id: String,
    @ColumnInfo(name = "provider_id") val providerId: String,
    @ColumnInfo(defaultValue = "''") val label: String,
    @ColumnInfo(name = "key_ref") val keyRef: String,
    @ColumnInfo(name = "hint_head") val hintHead: String,
    @ColumnInfo(name = "hint_tail") val hintTail: String,
    @ColumnInfo(name = "health_score", defaultValue = "1.0") val healthScore: Double,
    @ColumnInfo(name = "cooldown_until") val cooldownUntil: Long?,
    @ColumnInfo(name = "rpm_limit") val rpmLimit: Long?,
    @ColumnInfo(name = "tpm_limit") val tpmLimit: Long?,
    @ColumnInfo(name = "daily_request_limit") val dailyRequestLimit: Long?,
    @ColumnInfo(name = "failure_count", defaultValue = "0") val failureCount: Long,
    @ColumnInfo(defaultValue = "1") val enabled: Boolean,
    @ColumnInfo(name = "last_used_at") val lastUsedAt: Long?,
    @ColumnInfo(name = "created_at") val createdAt: Long,
    @ColumnInfo(name = "updated_at") val updatedAt: Long,
    @ColumnInfo(name = "deleted_at") val deletedAt: Long?
)

/** DDL `schema_meta`。 */
@Entity(
    tableName = "schema_meta",
)
internal data class SchemaMetaEntity(
    @PrimaryKey val key: String,
    val value: String
)

/** DDL `tag`。 */
@Entity(
    tableName = "tag",
)
internal data class TagEntity(
    @PrimaryKey val id: String,
    val name: String,
    @ColumnInfo(name = "created_at") val createdAt: Long
)

/** DDL `tombstone`。 */
@Entity(
    tableName = "tombstone",
    primaryKeys = ["entity", "entity_id"],
)
internal data class TombstoneEntity(
    val entity: String,
    @ColumnInfo(name = "entity_id") val entityId: String,
    @ColumnInfo(name = "deleted_at") val deletedAt: Long,
    val rev: String
)

/** DDL `tool_call_record`。 */
@Entity(
    tableName = "tool_call_record",
    foreignKeys = [
        ForeignKey(
            entity = MessageEntity::class,
            parentColumns = ["id"],
            childColumns = ["message_id"],
            onDelete = ForeignKey.CASCADE,
        ),
    ],
    indices = [
        Index(value = ["message_id", "call_index"], name = "idx_tool_call_record_message_id_call_index", unique = true),
    ],
)
@SuppressWarnings(RoomWarnings.MISSING_INDEX_ON_FOREIGN_KEY_CHILD)
internal data class ToolCallRecordEntity(
    @PrimaryKey val id: String,
    @ColumnInfo(name = "message_id") val messageId: String,
    @ColumnInfo(name = "call_index") val callIndex: Long,
    val name: String,
    @ColumnInfo(name = "args_json") val argsJson: String?,
    @ColumnInfo(name = "result_json") val resultJson: String?,
    @ColumnInfo(defaultValue = "'running'") val status: String,
    @ColumnInfo(name = "error_json") val errorJson: String?,
    @ColumnInfo(name = "started_at") val startedAt: Long,
    @ColumnInfo(name = "finished_at") val finishedAt: Long?
)

/** DDL `usage_record`。 */
@Entity(
    tableName = "usage_record",
    foreignKeys = [
        ForeignKey(
            entity = MessageEntity::class,
            parentColumns = ["id"],
            childColumns = ["message_id"],
            onDelete = ForeignKey.SET_NULL,
        ),
    ],
    indices = [
        Index(value = ["provider_id", "model_id", "created_at"], name = "idx_usage_model"),
    ],
)
@SuppressWarnings(RoomWarnings.MISSING_INDEX_ON_FOREIGN_KEY_CHILD)
internal data class UsageRecordEntity(
    @PrimaryKey val id: String,
    @ColumnInfo(name = "message_id") val messageId: String?,
    @ColumnInfo(name = "provider_id") val providerId: String?,
    @ColumnInfo(name = "model_id") val modelId: String?,
    @ColumnInfo(name = "conversation_id") val conversationId: String?,
    @ColumnInfo(name = "input_tokens", defaultValue = "0") val inputTokens: Long,
    @ColumnInfo(name = "output_tokens", defaultValue = "0") val outputTokens: Long,
    @ColumnInfo(name = "cached_tokens", defaultValue = "0") val cachedTokens: Long,
    @ColumnInfo(name = "reasoning_tokens", defaultValue = "0") val reasoningTokens: Long,
    @ColumnInfo(name = "cost_estimate") val costEstimate: Double?,
    @ColumnInfo(name = "provider_cost") val providerCost: Double?,
    @ColumnInfo(defaultValue = "'USD'") val currency: String,
    @ColumnInfo(name = "price_snapshot_json") val priceSnapshotJson: String?,
    @ColumnInfo(name = "is_estimate", defaultValue = "0") val isEstimate: Boolean,
    @ColumnInfo(name = "error_bound_pct") val errorBoundPct: Double?,
    @ColumnInfo(name = "created_at") val createdAt: Long
)
