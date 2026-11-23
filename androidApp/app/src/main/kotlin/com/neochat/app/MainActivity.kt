package com.neochat.app

import android.app.Activity
import android.content.Context
import android.os.Build
import android.os.Bundle
import android.view.WindowInsetsController
import androidx.compose.ui.platform.ComposeView
import com.neochat.app.chat.ChatViewModel
import com.neochat.app.chat.ProtocolChatEngine
import com.neochat.app.chat.ProviderConfigStore
import com.neochat.app.data.NeoChatDatabase
import com.neochat.app.data.RoomChatStore
import com.neochat.app.ui.chat.ChatScreen
import com.neochat.app.ui.theme.NeoChatTheme

/**
 * 唯一的 Activity。没有引入 `androidx.activity`：直接用 `ComposeView` 装内容，
 * 因此也不需要 AppCompat 主题（主题见 `res/values/themes.xml`）。
 */
public class MainActivity : Activity() {

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        ChatGraph.install(this)
        applyEdgeToEdge()
        setContentView(
            ComposeView(this).apply {
                setContent {
                    NeoChatTheme {
                        ChatScreen(viewModel = ChatGraph.viewModel, store = ChatGraph.store)
                    }
                }
            },
        )
    }

    /**
     * Android 15 起强制 edge-to-edge；这里显式关掉系统栏占位，改用 Compose 的 inset padding。
     * `setDecorFitsSystemWindows` 在 API 35 标记废弃但手机上仍要用它覆盖 30–34，
     * 与之等价的 `enableEdgeToEdge()` 在 `androidx.activity` 里，本里程碑不新增依赖（见交付偏差）。
     */
    @Suppress("DEPRECATION")
    private fun applyEdgeToEdge() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            window.setDecorFitsSystemWindows(false)
            window.insetsController?.setSystemBarsAppearance(
                WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS,
                WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS,
            )
        }
    }
}

/**
 * 进程级依赖图：配置、数据库与 ViewModel 都不随 Activity 重建而销毁（旋转屏幕不丢会话）。
 * 会话内容落在 `neochat.db`，进程结束只丢在飞的那次请求（下次启动扫成 `aborted`）。
 */
private object ChatGraph {

    private var application: Context? = null

    val store: ProviderConfigStore = ProviderConfigStore()

    private val database: NeoChatDatabase by lazy {
        NeoChatDatabase.open(requireNotNull(application) { "ChatGraph.install 未调用" })
    }

    val viewModel: ChatViewModel by lazy {
        ChatViewModel(
            engine = ProtocolChatEngine(provider = { store.config }),
            provider = store,
            store = RoomChatStore(database),
        )
    }

    fun install(context: Context) {
        if (application == null) application = context.applicationContext
    }
}
