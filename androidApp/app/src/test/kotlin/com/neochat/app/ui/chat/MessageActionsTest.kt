@file:Suppress("FunctionNaming")

package com.neochat.app.ui.chat

import com.neochat.app.chat.ChatMessageUi
import com.neochat.app.chat.MessageRole
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/** §3.5 长按菜单的行：复制全文 / 逐块复制 / 重新生成 / 软删。 */
class MessageActionsTest {

    @Test
    fun `流式回复不给重新生成与删除`() {
        val message = ChatMessageUi(id = 1, role = MessageRole.ASSISTANT, text = "在写", streaming = true)

        val kinds = actionsFor(message, 0).map { it.kind }

        assertEquals(listOf(MessageActionKind.COPY_ALL), kinds)
    }

    @Test
    fun `完成回复按顺序给出复制全文、逐块复制、重生成与删除`() {
        val message = ChatMessageUi(
            id = 2,
            role = MessageRole.ASSISTANT,
            text = "看这两段：\n\n```kotlin\nval a = 1\n```\n\n```sql\nselect 1;\n```\n",
        )

        val blocks = codeBlocksOf(message.text)
        val actions = actionsFor(message, blocks.size)

        assertEquals(2, blocks.size)
        assertEquals(listOf("val a = 1", "select 1;"), blocks)
        assertEquals(
            listOf(
                MessageActionKind.COPY_ALL,
                MessageActionKind.COPY_CODE,
                MessageActionKind.COPY_CODE,
                MessageActionKind.REGENERATE,
                MessageActionKind.DELETE,
            ),
            actions.map { it.kind },
        )
        assertEquals(listOf(0, 1), actions.filter { it.kind == MessageActionKind.COPY_CODE }.map { it.codeIndex })
    }

    @Test
    fun `引用里的代码块也能单独复制`() {
        val markdown = "> 先装包\n>\n> ```sh\n> adb install -r app.apk\n> ```\n"

        val blocks = codeBlocksOf(markdown)

        assertEquals(1, blocks.size)
        assertEquals("adb install -r app.apk", blocks.single().trim())
    }

    @Test
    fun `没有正文的空回复只给重生成与删除`() {
        val message = ChatMessageUi(id = 3, role = MessageRole.ASSISTANT, text = "")

        val kinds = actionsFor(message, codeBlocksOf(message.text).size).map { it.kind }

        assertEquals(listOf(MessageActionKind.REGENERATE, MessageActionKind.DELETE), kinds)
        assertTrue(codeBlocksOf("").isEmpty())
    }
}
