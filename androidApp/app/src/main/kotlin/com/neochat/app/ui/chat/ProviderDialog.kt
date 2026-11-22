@file:Suppress("FunctionNaming")

package com.neochat.app.ui.chat

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import com.neochat.app.R
import com.neochat.app.chat.ProviderConfig
import com.neochat.app.chat.ProviderField
import com.neochat.app.ui.theme.Metrics
import com.neochat.app.ui.theme.Space

/** 对话框里正在编辑的三个字段，避免把六七个 `remember` 摊在签名上。 */
private class ProviderDraft(initial: ProviderConfig) {
    var baseUrl: String by mutableStateOf(initial.baseUrl)
    var apiKey: String by mutableStateOf(initial.apiKey)
    var model: String by mutableStateOf(initial.model)

    fun toConfig(): ProviderConfig = ProviderConfig(baseUrl = baseUrl, apiKey = apiKey, model = model)
}

/**
 * Provider 配置对话框：Base URL / API Key / 模型 ID。
 * `edit_provider` 的 `focus` 参数决定先聚焦哪个字段（spec/errors.md §4）。
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
public fun ProviderDialog(
    initial: ProviderConfig,
    focus: ProviderField,
    onDismiss: () -> Unit,
    onSave: (ProviderConfig) -> Unit,
) {
    val draft = remember(initial) { ProviderDraft(initial) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = stringResource(R.string.provider_title)) },
        text = { ProviderFields(draft = draft, focus = focus) },
        confirmButton = {
            TextButton(onClick = { onSave(draft.toConfig()) }) {
                Text(stringResource(R.string.provider_save))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(R.string.provider_cancel))
            }
        },
    )
}

/**
 * 三个输入位。密钥用被标记废弃的 `PasswordVisualTransformation`：`BasicSecureTextField` 与
 * `OutlinedTextField` 的组合还不稳定，迁移排进输入生态里程碑（见交付偏差）。
 */
@Suppress("DEPRECATION")
@Composable
private fun ProviderFields(draft: ProviderDraft, focus: ProviderField) {
    val baseUrlFocus = remember { FocusRequester() }
    val apiKeyFocus = remember { FocusRequester() }
    val modelFocus = remember { FocusRequester() }
    LaunchedEffect(focus) {
        when (focus) {
            ProviderField.BASE_URL -> baseUrlFocus
            ProviderField.API_KEY -> apiKeyFocus
            ProviderField.MODEL -> modelFocus
        }.requestFocus()
    }
    Column(
        modifier = Modifier.padding(top = Metrics.dialogPadding),
        verticalArrangement = Arrangement.spacedBy(Space.md),
    ) {
        Text(text = stringResource(R.string.provider_hint))
        OutlinedTextField(
            value = draft.baseUrl,
            onValueChange = { draft.baseUrl = it },
            modifier = Modifier.fillMaxWidth().focusRequester(baseUrlFocus),
            label = { Text(stringResource(R.string.provider_base_url)) },
            singleLine = true,
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
        )
        OutlinedTextField(
            value = draft.apiKey,
            onValueChange = { draft.apiKey = it },
            modifier = Modifier.fillMaxWidth().focusRequester(apiKeyFocus),
            label = { Text(stringResource(R.string.provider_api_key)) },
            singleLine = true,
            visualTransformation = PasswordVisualTransformation(),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
        )
        OutlinedTextField(
            value = draft.model,
            onValueChange = { draft.model = it },
            modifier = Modifier.fillMaxWidth().focusRequester(modelFocus),
            label = { Text(stringResource(R.string.provider_model)) },
            singleLine = true,
        )
    }
}

/** 脱敏后的原始响应：只在用户点 `view_raw` 时出现。 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
public fun RawErrorDialog(raw: String, onDismiss: () -> Unit) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(R.string.error_raw_title)) },
        text = {
            androidx.compose.foundation.text.selection.SelectionContainer {
                Text(text = raw, modifier = Modifier.padding(vertical = Space.sm))
            }
        },
        confirmButton = {
            TextButton(onClick = onDismiss) { Text(stringResource(R.string.error_close)) }
        },
    )
}
