package com.neochat.app.chat

import com.neochat.app.data.StoredMessage
import com.neochat.app.data.StoredRole
import com.neochat.app.data.StoredStatus
import com.neochat.protocol.FinishReason
import com.neochat.protocol.StreamEvent
import com.neochat.protocol.request.ChatRequest
import com.neochat.protocol.request.ChatRole
import com.neochat.protocol.request.MessagePart
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/** ViewModel 只负责"意图 → 请求 → 事件回灌"，用假引擎在 JVM 上跑通。 */
class ChatViewModelTest {

    private class FakeEngine : ChatEngine {
        val requests: MutableList<ChatRequest> = mutableListOf()
        val callbacks: MutableList<(StreamEvent) -> Unit> = mutableListOf()
        var cancelCount: Int = 0
        var closed: Boolean = false
        var startFailure: Throwable? = null

        override fun start(request: ChatRequest, onEvent: (StreamEvent) -> Unit): ChatRun {
            startFailure?.let { throw it }
            requests += request
            callbacks += onEvent
            return ChatRun { cancelCount += 1 }
        }

        override fun close() {
            closed = true
        }

        fun emit(index: Int, event: StreamEvent) {
            callbacks[index](event)
        }
    }

    private val provider = ProviderConfigStore(
        ProviderConfig(baseUrl = "https://api.example.com/v1", apiKey = "unit-test-key", model = "deepseek-chat"),
    )

    private val store = FakeChatStore()

    private fun viewModel(engine: FakeEngine): ChatViewModel =
        ChatViewModel(
            engine = engine,
            provider = provider,
            store = store,
            scope = CoroutineScope(Dispatchers.Unconfined),
        )

    @Test
    fun `提交把历史和模型交给请求层，流事件推进到就绪`() {
        val engine = FakeEngine()
        val vm = viewModel(engine)
        vm.startSession()
        vm.updateDraft("你好")
        vm.submit()

        assertEquals(1, engine.requests.size)
        val request = engine.requests.single()
        assertEquals("deepseek-chat", request.model)
        assertEquals(1, request.messages.size)
        assertEquals(ChatRole.USER, request.messages.single().role)
        assertEquals(MessagePart.Text("你好"), request.messages.single().parts.single())
        assertEquals(ChatPhase.CONNECTING, vm.state.value.phase)

        engine.emit(0, StreamEvent.Delta(0, "你"))
        engine.emit(0, StreamEvent.Delta(1, "好"))
        assertEquals(ChatPhase.STREAMING, vm.state.value.phase)
        assertEquals("你好", vm.state.value.messages.last().text)

        engine.emit(0, StreamEvent.Done(2, FinishReason.STOP))
        assertEquals(ChatPhase.IDLE, vm.state.value.phase)
        assertFalse(vm.state.value.messages.last().streaming)
        assertEquals("", vm.state.value.draft)
    }

    @Test
    fun `停止取消流并把阶段落到已停止`() {
        val engine = FakeEngine()
        val vm = viewModel(engine)
        vm.startSession()
        vm.updateDraft("长回答")
        vm.submit()
        engine.emit(0, StreamEvent.Delta(0, "开头"))

        vm.stop()

        assertEquals(1, engine.cancelCount)
        assertEquals(ChatPhase.STOPPED, vm.state.value.phase)
        assertTrue(vm.state.value.messages.last().interrupted)
        assertEquals("开头", vm.state.value.messages.last().text)
    }

    @Test
    fun `停止之后迟到的流事件被丢弃`() {
        val engine = FakeEngine()
        val vm = viewModel(engine)
        vm.startSession()
        vm.updateDraft("长回答")
        vm.submit()
        engine.emit(0, StreamEvent.Delta(0, "开头"))
        vm.stop()

        engine.emit(0, StreamEvent.Delta(1, "不该出现"))
        engine.emit(0, StreamEvent.Done(2, FinishReason.STOP))

        assertEquals("开头", vm.state.value.messages.last().text)
        assertEquals(ChatPhase.STOPPED, vm.state.value.phase)
    }

    @Test
    fun `请求层开不起来时把 INTERNAL 错误落到这一轮`() {
        val engine = FakeEngine()
        engine.startFailure = IllegalStateException("boom")
        val vm = viewModel(engine)
        vm.startSession()
        vm.updateDraft("你好")
        vm.submit()

        assertEquals(ChatPhase.FAILED, vm.state.value.phase)
        val failed = vm.state.value.messages.last()
        assertNotNull(failed.error)
        assertEquals("INTERNAL", failed.error?.code?.wire)
        assertTrue(failed.error?.actions?.isNotEmpty() == true)
    }

    @Test
    fun `冷启动打开最近的会话，把历史装进状态`() {
        store.latestConversationId = "conversation-cold"
        store.restoreMessages = listOf(
            StoredMessage("m1", StoredRole.USER, "上一轮的提问", "", StoredStatus.COMPLETE),
            StoredMessage("m2", StoredRole.ASSISTANT, "上一轮的答复", "想了想", StoredStatus.ABORTED),
        )
        val vm = viewModel(FakeEngine())

        assertEquals("conversation-cold", vm.state.value.conversationId)
        assertEquals(listOf(MessageRole.USER, MessageRole.ASSISTANT), vm.state.value.messages.map { it.role })
        assertEquals("上一轮的答复", vm.state.value.messages.last().text)
        assertEquals("想了想", vm.state.value.messages.last().reasoning)
        assertTrue(vm.state.value.messages.last().interrupted)
        assertEquals(ChatPhase.IDLE, vm.state.value.phase)
    }

    @Test
    fun `提交会先落盘再开流，退出的轮次落成 aborted`() {
        val engine = FakeEngine()
        val vm = viewModel(engine)
        vm.startSession()
        vm.updateDraft("你好")
        vm.submit()
        assertEquals(listOf("conversation-1"), store.created)
        engine.emit(0, StreamEvent.Delta(0, "半句"))
        assertEquals("半句", store.appendedText.toString())

        vm.stop()

        assertEquals(listOf(StoredStatus.ABORTED), store.finished)
    }

    @Test
    fun `第二轮请求带上第一轮的成功历史`() {
        val engine = FakeEngine()
        val vm = viewModel(engine)
        vm.startSession()
        vm.updateDraft("第一问")
        vm.submit()
        engine.emit(0, StreamEvent.Delta(0, "第一答"))
        engine.emit(0, StreamEvent.Done(1, FinishReason.STOP))

        vm.updateDraft("第二问")
        vm.submit()

        val second = engine.requests[1]
        assertEquals(
            listOf(ChatRole.USER, ChatRole.ASSISTANT, ChatRole.USER),
            second.messages.map { it.role },
        )
        assertEquals(
            listOf("第一问", "第一答", "第二问"),
            second.messages.map { (it.parts.single() as MessagePart.Text).text },
        )
    }
}
