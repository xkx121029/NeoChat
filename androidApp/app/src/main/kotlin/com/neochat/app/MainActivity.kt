package com.neochat.app

import android.app.Activity
import android.content.Context
import android.content.res.Configuration
import android.os.Build
import android.os.Bundle
import android.view.WindowInsetsController
import androidx.compose.ui.platform.ComposeView
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.setViewTreeLifecycleOwner
import androidx.savedstate.SavedStateRegistry
import androidx.savedstate.SavedStateRegistryController
import androidx.savedstate.SavedStateRegistryOwner
import androidx.savedstate.setViewTreeSavedStateRegistryOwner
import com.neochat.app.chat.ChatViewModel
import com.neochat.app.chat.ProtocolChatEngine
import com.neochat.app.chat.ProviderConfigStore
import com.neochat.app.data.KeystoreSecretSealer
import com.neochat.app.data.NeoChatDatabase
import com.neochat.app.data.RoomChatStore
import com.neochat.app.data.SharedPreferencesProviderSettings
import com.neochat.app.ui.chat.ChatScreen
import com.neochat.app.ui.theme.NeoChatTheme

/**
 * 唯一的 Activity。没有引入 `androidx.activity`：直接用 `ComposeView` 装内容，
 * 因此也不需要 AppCompat 主题（主题见 `res/values/themes.xml`）。
 *
 * 裸 `Activity` 既不是 `LifecycleOwner` 也不是 `SavedStateRegistryOwner`，而 `ComposeView`
 * 挂到窗口时两个都要从视图树里取（`ViewTreeLifecycleOwner` 建 recomposer，
 * `ViewTreeSavedStateRegistryOwner` 供 `rememberSaveable`）。这里自己持有这两个 registry
 * 并挂到内容视图上，避免为此引入 `androidx.activity`（NEOC-18 模拟器首启依次暴露这两个缺失）。
 */
public class MainActivity : Activity(), LifecycleOwner, SavedStateRegistryOwner {

    private val registry: LifecycleRegistry = LifecycleRegistry(this)

    private val savedState: SavedStateRegistryController = SavedStateRegistryController.create(this)

    override val lifecycle: Lifecycle
        get() = registry

    override val savedStateRegistry: SavedStateRegistry
        get() = savedState.savedStateRegistry

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // performRestore 必须在生命周期离开 INITIALIZED 之前：它要按 owner 的初始状态建 Restarter。
        savedState.performRestore(savedInstanceState)
        registry.currentState = Lifecycle.State.CREATED
        ChatGraph.install(this)
        setContentView(
            ComposeView(this).apply {
                setViewTreeLifecycleOwner(this@MainActivity)
                setViewTreeSavedStateRegistryOwner(this@MainActivity)
                setContent {
                    NeoChatTheme {
                        ChatScreen(viewModel = ChatGraph.viewModel, store = ChatGraph.store)
                    }
                }
            },
        )
        applyEdgeToEdge()
    }

    override fun onStart() {
        super.onStart()
        registry.currentState = Lifecycle.State.STARTED
    }

    override fun onResume() {
        super.onResume()
        registry.currentState = Lifecycle.State.RESUMED
    }

    override fun onPause() {
        registry.currentState = Lifecycle.State.STARTED
        super.onPause()
    }

    override fun onStop() {
        registry.currentState = Lifecycle.State.CREATED
        super.onStop()
    }

    override fun onSaveInstanceState(outState: Bundle) {
        super.onSaveInstanceState(outState)
        savedState.performSave(outState)
    }

    override fun onDestroy() {
        registry.currentState = Lifecycle.State.DESTROYED
        super.onDestroy()
    }

    /**
     * Android 15 起强制 edge-to-edge；这里显式关掉系统栏占位，改用 Compose 的 inset padding。
     * `setDecorFitsSystemWindows` 在 API 35 标记废弃但手机上仍要用它覆盖 30–34，
     * 与之等价的 `enableEdgeToEdge()` 在 `androidx.activity` 里，本里程碑不新增依赖（见交付偏差）。
     *
     * 必须在 `setContentView` 之后调用：`window.insetsController` 要等 decorView 建好才有值，
     * 提前取会在 API 30–33 上抛 NPE（NEOC-18 模拟器首启暴露）。
     */
    @Suppress("DEPRECATION")
    private fun applyEdgeToEdge() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            window.setDecorFitsSystemWindows(false)
            applySystemBarAppearance()
        }
    }

    /** 系统栏图标跟着深浅色走：亮色页面深色图标，暗色页面浅色图标。 */
    private fun applySystemBarAppearance() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.R) return
        val dark = resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK ==
            Configuration.UI_MODE_NIGHT_YES
        val mask = WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS or
            WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS
        window.insetsController?.setSystemBarsAppearance(if (dark) 0 else mask, mask)
    }
}

/**
 * 进程级依赖图：配置、数据库与 ViewModel 都不随 Activity 重建而销毁（旋转屏幕不丢会话）。
 * 会话内容落在 `neochat.db`，进程结束只丢在飞的那次请求（下次启动扫成 `aborted`）。
 *
 * provider 配置在这里从磁盘读回：`SharedPreferences` 的 base_url / model 是明文，
 * 密钥是 Keystore 封装后的密文（ADR-002），读回来就自动选中，冷启动不用重填。
 */
private object ChatGraph {

    private var application: Context? = null

    private val settings: SharedPreferencesProviderSettings by lazy {
        SharedPreferencesProviderSettings(
            context = requireNotNull(application) { "ChatGraph.install 未调用" },
            sealer = KeystoreSecretSealer(KEY_ALIAS),
        )
    }

    val store: ProviderConfigStore by lazy { ProviderConfigStore(settings.load(), settings) }

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

    /** ADR-002 §3：`provider_key.key_ref` 指向的就是这个 Keystore 条目。 */
    private const val KEY_ALIAS: String = "provider_key_default"
}
