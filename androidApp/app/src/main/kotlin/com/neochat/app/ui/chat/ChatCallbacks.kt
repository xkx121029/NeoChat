@file:Suppress("FunctionNaming")

package com.neochat.app.ui.chat

import com.neochat.protocol.errors.ErrorAction
import com.neochat.protocol.errors.ErrorObject

/** 空态与顶栏能发起的意图。 */
internal class ScreenCallbacks(
    val onStart: () -> Unit,
    val onSuggestion: (String) -> Unit,
    val onNewChat: () -> Unit,
    val onOpenProvider: () -> Unit,
    val onAction: (ErrorObject, ErrorAction) -> Unit,
    val onDismissError: (Long) -> Unit,
)

/** 输入栏能发起的意图。 */
internal class InputCallbacks(
    val onDraftChange: (String) -> Unit,
    val onSend: () -> Unit,
    val onStop: () -> Unit,
)
