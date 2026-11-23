package com.neochat.app.chat

import com.neochat.protocol.FinishReason
import com.neochat.protocol.StreamEvent
import com.neochat.protocol.UsageCounts
import com.neochat.protocol.errors.ErrorMapper
import com.neochat.protocol.errors.ProviderFacts
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/** 用一串 `StreamEvent` 直接断言状态机产出的消息状态序列。 */
class ChatStateMachineTest {

    private val facts = ProviderFacts(provider = "openai_compatible", model = "deepseek-chat")

    @Test
    fun `正常一轮：连接中 → 流式中 → 就绪，文本按到达顺序拼接`() {
        val machine = ChatStateMachine()
        machine.updateDraft("你好")
        val submission = machine.submit()

        assertNotNull(submission)
        assertEquals(ChatPhase.CONNECTING, machine.state.phase)
        assertEquals(2, machine.state.messages.size)
        assertEquals(MessageRole.USER, machine.state.messages[0].role)
        assertEquals("你好", machine.state.messages[0].text)
        assertTrue(machine.state.messages[1].streaming)

        val phases = mutableListOf<ChatPhase>()
        val events = listOf(
            StreamEvent.ReasoningDelta(0, "先想"),
            StreamEvent.ReasoningDelta(1, "一下"),
            StreamEvent.Delta(2, "你"),
            StreamEvent.Delta(3, "好"),
            StreamEvent.Usage(4, UsageCounts(input = 8, output = 2)),
            StreamEvent.Done(5, FinishReason.STOP),
        )
        events.forEach { event ->
            machine.onStreamEvent(event)
            phases += machine.state.phase
        }

        val expectedPhases = listOf(
            ChatPhase.STREAMING,
            ChatPhase.STREAMING,
            ChatPhase.STREAMING,
            ChatPhase.STREAMING,
            ChatPhase.STREAMING,
            ChatPhase.IDLE,
        )
        assertEquals(expectedPhases, phases)
        val assistant = machine.state.messages.last()
        assertEquals("你好", assistant.text)
        assertEquals("先想一下", assistant.reasoning)
        assertFalse(assistant.streaming)
        assertEquals(2L, assistant.usage?.output)
        assertNull(assistant.error)
    }

    @Test
    fun `中途取消：立即落到已停止并标记中断，随后到达的 done 不回退`() {
        val machine = ChatStateMachine()
        machine.updateDraft("写一段长文")
        machine.submit()
        machine.onStreamEvent(StreamEvent.Delta(0, "第一段"))

        machine.requestCancel()
        assertEquals(ChatPhase.STOPPED, machine.state.phase)
        val cancelled = machine.state.messages.last()
        assertTrue(cancelled.interrupted)
        assertFalse(cancelled.streaming)
        assertEquals("第一段", cancelled.text)

        machine.onStreamEvent(StreamEvent.Done(1, FinishReason.ABORTED_USER))
        assertEquals(ChatPhase.STOPPED, machine.state.phase)
        assertTrue(machine.state.messages.last().interrupted)
    }

    @Test
    fun `错误落盘：failed 事件把错误留在这一轮，关横幅后仍在`() {
        val machine = ChatStateMachine()
        machine.updateDraft("你好")
        machine.submit()
        machine.onStreamEvent(StreamEvent.Delta(0, "半句"))

        val body = """{"error":{"message":"Incorrect API key provided",""" +
            """"type":"invalid_request_error","code":"invalid_api_key"}}"""
        val error = ErrorMapper.fromHttp(status = 401, headers = emptyMap(), bodyText = body, facts = facts)
        machine.onStreamEvent(StreamEvent.Failed(1, error))

        assertEquals(ChatPhase.FAILED, machine.state.phase)
        val failed = machine.state.messages.last()
        assertNotNull(failed.error)
        assertFalse(failed.streaming)
        assertTrue(failed.errorBannerVisible)
        assertEquals("AUTH_INVALID_KEY", failed.error?.code?.wire)
        assertEquals("半句", failed.text)
        assertTrue(failed.error?.actions?.any { it.a == "edit_provider" } == true)

        machine.dismissError(failed.id)
        val dismissed = machine.state.messages.last()
        assertNotNull(dismissed.error)
        assertFalse(dismissed.errorBannerVisible)
        assertEquals(ChatPhase.FAILED, machine.state.phase)
    }

    @Test
    fun `重试沿用同一份历史：丢掉失败的那条助手消息，从最后一个用户消息重开`() {
        val machine = ChatStateMachine()
        machine.updateDraft("第一问")
        machine.submit()
        machine.onStreamEvent(StreamEvent.Done(0, FinishReason.STOP))
        machine.updateDraft("第二问")
        val second = machine.submit()
        machine.onStreamEvent(StreamEvent.Failed(1, ErrorMapper.internal(facts, "engine-start")))
        assertEquals(ChatPhase.FAILED, machine.state.phase)

        val retry = machine.retry()

        assertNotNull(retry)
        assertEquals(second?.history, retry?.history)
        assertEquals("第二问", retry?.text)
        assertEquals(ChatPhase.CONNECTING, machine.state.phase)
        assertEquals(4, machine.state.messages.size)
        assertEquals(MessageRole.ASSISTANT, machine.state.messages.last().role)
        assertTrue(machine.state.messages.last().streaming)
        assertEquals(null, machine.state.messages.last().error)
    }

    @Test
    fun `重新生成从该回复重开：历史截到它回答的用户消息`() {
        val machine = ChatStateMachine()
        machine.updateDraft("第一问")
        machine.submit()
        machine.onStreamEvent(StreamEvent.Done(0, FinishReason.STOP))
        machine.updateDraft("第二问")
        machine.submit()
        machine.onStreamEvent(StreamEvent.Done(1, FinishReason.STOP))
        val secondReply = machine.state.messages.last()

        val submission = machine.regenerate(secondReply.id)

        assertNotNull(submission)
        assertEquals("第二问", submission?.text)
        assertEquals(
            listOf(ChatTurn(MessageRole.USER, "第一问"), ChatTurn(MessageRole.USER, "第二问")),
            submission?.history,
        )
        assertEquals(ChatPhase.CONNECTING, machine.state.phase)
        assertEquals(4, machine.state.messages.size)
        assertEquals(MessageRole.ASSISTANT, machine.state.messages.last().role)
        assertTrue(machine.state.messages.last().streaming)
        assertEquals(secondReply.id + 1, machine.state.messages.last().id)
    }

    @Test
    fun `重新生成第一条回复时历史只剩它回答的那一问`() {
        val machine = ChatStateMachine()
        machine.updateDraft("唯一一问")
        machine.submit()
        machine.onStreamEvent(StreamEvent.Delta(0, "答"))
        machine.onStreamEvent(StreamEvent.Done(1, FinishReason.STOP))
        val firstReply = machine.state.messages.last()

        val submission = machine.regenerate(firstReply.id)

        assertEquals(listOf(ChatTurn(MessageRole.USER, "唯一一问")), submission?.history)
        assertEquals(2, machine.state.messages.size)
    }

    @Test
    fun `重新生成在流式期间被拒绝`() {
        val machine = ChatStateMachine()
        machine.updateDraft("问")
        machine.submit()

        assertNull(machine.regenerate(machine.state.messages.last().id))
    }

    @Test
    fun `删除一条消息时连同它的分支一起从界面撤掉`() {
        val machine = ChatStateMachine()
        machine.updateDraft("第一问")
        machine.submit()
        machine.onStreamEvent(StreamEvent.Done(0, FinishReason.STOP))
        machine.updateDraft("第二问")
        machine.submit()
        machine.onStreamEvent(StreamEvent.Done(1, FinishReason.STOP))
        val secondQuestion = machine.state.messages[2]

        machine.deleteMessage(secondQuestion.id)

        assertEquals(2, machine.state.messages.size)
        assertEquals(listOf("第一问", ""), machine.state.messages.map { it.text })
        assertEquals(ChatPhase.IDLE, machine.state.phase)
    }

    @Test
    fun `流式中的回复不被删除`() {
        val machine = ChatStateMachine()
        machine.updateDraft("问")
        machine.submit()

        machine.deleteMessage(machine.state.messages.last().id)

        assertEquals(2, machine.state.messages.size)
        assertEquals(ChatPhase.CONNECTING, machine.state.phase)
    }

    @Test
    fun `空输入不发请求，重复提交被丢弃`() {
        val machine = ChatStateMachine()
        machine.updateDraft("   ")
        assertNull(machine.submit())
        assertEquals(ChatPhase.IDLE, machine.state.phase)
        assertTrue(machine.state.messages.isEmpty())

        machine.updateDraft("有内容")
        assertNotNull(machine.submit())
        assertNull(machine.submit())
    }
}
