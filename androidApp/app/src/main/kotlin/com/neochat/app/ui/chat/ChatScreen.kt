@file:Suppress("FunctionNaming")

package com.neochat.app.ui.chat

import android.content.ClipData
import android.content.Context
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.systemBars
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import com.neochat.app.R
import com.neochat.app.chat.ChatMessageUi
import com.neochat.app.chat.ChatUiState
import com.neochat.app.chat.ChatViewModel
import com.neochat.app.chat.ProviderConfigStore
import com.neochat.app.chat.ProviderField
import com.neochat.app.ui.theme.LocalNeoColors
import com.neochat.app.ui.theme.Space
import com.neochat.protocol.errors.ErrorAction
import com.neochat.protocol.errors.ErrorObject
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.launch

/**
 * 普通对话整屏：渐变页面底（L01→L02）、顶栏、消息列表/空态、输入栏，外加配置对话框与提示条。
 * 这里只做"把意图转给 ViewModel + 把状态画出来"，没有任何请求逻辑。
 */
@Composable
public fun ChatScreen(viewModel: ChatViewModel, store: ProviderConfigStore) {
    val state by viewModel.state.collectAsState()
    val conversations by viewModel.conversations.collectAsState()
    var listOpen by remember { mutableStateOf(false) }
    val context = LocalContext.current
    val snackbarHost = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()
    val unavailableTemplate = stringResource(R.string.error_action_unavailable)
    val copiedNotice = stringResource(R.string.error_diagnostics_copied)
    var providerFocus by remember { mutableStateOf<ProviderField?>(null) }
    var rawText by remember { mutableStateOf<String?>(null) }
    var menuTarget by remember { mutableStateOf<ChatMessageUi?>(null) }
    val messageCopiedNotice = stringResource(R.string.chat_copied)
    val codeCopiedNotice = stringResource(R.string.chat_copied_code)
    val messageDeletedNotice = stringResource(R.string.chat_deleted)

    fun requireProvider() {
        providerFocus = store.focusField(emptyMap())
    }

    val screen = ScreenCallbacks(
        onStart = {
            viewModel.startSession()
            if (!store.isReady) requireProvider()
        },
        onSuggestion = { text ->
            viewModel.updateDraft(text)
            if (store.isReady) viewModel.submit() else requireProvider()
        },
        onNewChat = viewModel::startNewConversation,
        onOpenProvider = ::requireProvider,
        onAction = { error, action ->
            when (val effect = effectOf(error, action, store, context)) {
                ActionEffect.Retry -> viewModel.retry()
                is ActionEffect.Configure -> providerFocus = effect.focus
                is ActionEffect.Copied -> copyDiagnostics(context, effect, copiedNotice, scope, snackbarHost)
                is ActionEffect.ShowRaw -> rawText = effect.text
                is ActionEffect.Unavailable ->
                    scope.launch { snackbarHost.showSnackbar(unavailableTemplate.format(effect.actionLabel)) }
            }
        },
        onDismissError = viewModel::dismissError,
    )

    val input = InputCallbacks(
        onDraftChange = viewModel::updateDraft,
        onSend = { if (store.isReady) viewModel.submit() else requireProvider() },
        onStop = viewModel::stop,
    )

    ChatFrame(
        state = state,
        modelName = store.config.model.ifBlank { stringResource(R.string.chat_model_unset) },
        screen = screen,
        input = input,
        snackbarHost = snackbarHost,
        onOpenList = { listOpen = true },
        onLongPressMessage = { menuTarget = it },
    )

    if (listOpen) {
        ConversationPanel(
            conversations = conversations,
            activeId = state.conversationId,
            now = remember(listOpen) { System.currentTimeMillis() },
            onSelect = { id ->
                listOpen = false
                viewModel.openConversation(id)
            },
            onNew = {
                listOpen = false
                viewModel.startNewConversation()
                if (!store.isReady) requireProvider()
            },
            onDelete = viewModel::deleteConversation,
            onClose = { listOpen = false },
        )
    }

    providerFocus?.let { focus ->
        ProviderDialog(
            initial = store.config,
            focus = focus,
            onDismiss = { providerFocus = null },
            onSave = { config ->
                store.update(config)
                providerFocus = null
            },
        )
    }
    rawText?.let { raw ->
        RawErrorDialog(raw = raw, onDismiss = { rawText = null })
    }
    menuTarget?.let { message ->
        val codeBlocks = remember(message.id, message.text) { codeBlocksOf(message.text) }
        MessageMenu(
            message = message,
            codeBlocks = codeBlocks,
            onDismiss = { menuTarget = null },
            onCopyAll = {
                copyPlainText(context, message.text)
                menuTarget = null
                scope.launch { snackbarHost.showSnackbar(messageCopiedNotice) }
            },
            onCopyCode = { index ->
                codeBlocks.getOrNull(index)?.let { copyPlainText(context, it) }
                menuTarget = null
                scope.launch { snackbarHost.showSnackbar(codeCopiedNotice) }
            },
            onRegenerate = {
                viewModel.regenerate(message.id)
                menuTarget = null
            },
            onDelete = {
                viewModel.deleteMessage(message.id)
                menuTarget = null
                scope.launch { snackbarHost.showSnackbar(messageDeletedNotice) }
            },
        )
    }
}

/** 一屏三块：顶栏、内容区、输入栏；内容区在空态与消息列表之间二选一。 */
@Composable
private fun ChatFrame(
    state: ChatUiState,
    modelName: String,
    screen: ScreenCallbacks,
    input: InputCallbacks,
    snackbarHost: SnackbarHostState,
    onOpenList: () -> Unit,
    onLongPressMessage: (ChatMessageUi) -> Unit,
) {
    val colors = LocalNeoColors.current
    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(Brush.verticalGradient(listOf(colors.pageTop, colors.pageBottom)))
            .windowInsetsPadding(WindowInsets.systemBars)
            .imePadding(),
    ) {
        Column(modifier = Modifier.fillMaxSize()) {
            ChatTopBar(
                phase = state.phase,
                modelName = modelName,
                onOpenList = onOpenList,
                onOpenProvider = screen.onOpenProvider,
            )
            Box(modifier = Modifier.weight(1f), contentAlignment = Alignment.Center) {
                if (state.messages.isEmpty()) {
                    EmptyState(onStart = screen.onStart, onSuggestion = screen.onSuggestion)
                } else {
                    MessageList(
                        messages = state.messages,
                        onAction = screen.onAction,
                        onDismissError = screen.onDismissError,
                        onLongPressMessage = onLongPressMessage,
                    )
                }
            }
            Box(modifier = Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
                Composer(
                    state = state,
                    onDraftChange = input.onDraftChange,
                    onSend = input.onSend,
                    onStop = input.onStop,
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = Space.gutter)
                        .padding(bottom = Space.md)
                        .widthIn(max = Space.readingWidth),
                )
            }
        }
        SnackbarHost(
            hostState = snackbarHost,
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .padding(horizontal = Space.gutter, vertical = Space.xxl),
        )
    }
}

private fun copyDiagnostics(
    context: Context,
    effect: ActionEffect.Copied,
    notice: String,
    scope: CoroutineScope,
    snackbarHost: SnackbarHostState,
) {
    copyPlainText(context, effect.text, effect.label)
    scope.launch { snackbarHost.showSnackbar(notice) }
}

/** 写系统剪贴板；标签只用于无障碍朗读，不影响粘贴内容。 */
private fun copyPlainText(context: Context, text: String, label: String = "") {
    val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as android.content.ClipboardManager
    clipboard.setPrimaryClip(ClipData.newPlainText(label, text))
}
