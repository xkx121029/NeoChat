@file:Suppress("FunctionNaming")

package com.neochat.app.ui.chat

import android.content.Context
import com.neochat.app.chat.ProviderConfigStore
import com.neochat.app.chat.ProviderField
import com.neochat.protocol.errors.ErrorAction
import com.neochat.protocol.errors.ErrorObject

/** 点一个错误操作之后界面该做什么；纯数据，解析层不碰 UI。 */
internal sealed interface ActionEffect {
    data object Retry : ActionEffect
    data class Configure(val focus: ProviderField) : ActionEffect
    data class Copied(val text: String, val label: String) : ActionEffect
    data class ShowRaw(val text: String) : ActionEffect
    data class Unavailable(val actionLabel: String) : ActionEffect
}

/**
 * spec/errors.md §4 的 action 闭集 → 本里程碑能落地的效果。
 * 闭集里没有实现的（充值页、帮助页、MCP 设置等）返回 [ActionEffect.Unavailable]，界面给提示但不会崩。
 */
internal fun effectOf(
    error: ErrorObject,
    action: ErrorAction,
    store: ProviderConfigStore,
    context: Context,
): ActionEffect = when (action.a) {
    ACTION_RETRY, ACTION_RETRY_AFTER -> ActionEffect.Retry

    ACTION_EDIT_PROVIDER,
    ACTION_OPEN_PROVIDER_SETTINGS,
    ACTION_MANAGE_KEYS,
    ACTION_CHECK_BASE_URL,
    ACTION_CHECK_PROXY,
    ACTION_SWITCH_MODEL,
    -> ActionEffect.Configure(store.focusField(ErrorCopy.params(action)))

    ACTION_COPY_DIAGNOSTICS -> ActionEffect.Copied(diagnostics(error), diagnosticsLabel(error))

    ACTION_VIEW_RAW -> ActionEffect.ShowRaw(error.raw ?: error.providerCode ?: error.code.wire)

    else -> ActionEffect.Unavailable(ErrorCopy.actionLabel(context, error, action))
}

/** 复制诊断：只放脱敏后的字段，不出现完整 URL 查询串或用户消息。 */
private fun diagnostics(error: ErrorObject): String = buildString {
    appendLine("code=${error.code.wire}")
    appendLine("stage=${error.stage.wire}")
    appendLine("retryable=${error.retryable}")
    error.provider?.let { appendLine("provider=$it") }
    error.model?.let { appendLine("model=$it") }
    error.httpStatus?.let { appendLine("http_status=$it") }
    error.providerCode?.let { appendLine("provider_code=$it") }
    error.requestId?.let { appendLine("request_id=$it") }
    appendLine("trace_id=${error.traceId}")
    error.raw?.let { appendLine("raw=$it") }
}

private fun diagnosticsLabel(error: ErrorObject): String = "NeoChat ${error.code.wire} ${error.traceId}"

private const val ACTION_RETRY: String = "retry"
private const val ACTION_RETRY_AFTER: String = "retry_after"
private const val ACTION_EDIT_PROVIDER: String = "edit_provider"
private const val ACTION_OPEN_PROVIDER_SETTINGS: String = "open_provider_settings"
private const val ACTION_MANAGE_KEYS: String = "manage_keys"
private const val ACTION_CHECK_BASE_URL: String = "check_base_url"
private const val ACTION_CHECK_PROXY: String = "check_proxy"
private const val ACTION_SWITCH_MODEL: String = "switch_model"
private const val ACTION_COPY_DIAGNOSTICS: String = "copy_diagnostics"
private const val ACTION_VIEW_RAW: String = "view_raw"
