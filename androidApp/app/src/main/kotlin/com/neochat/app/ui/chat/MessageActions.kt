@file:Suppress("FunctionNaming")

package com.neochat.app.ui.chat

import com.neochat.app.chat.ChatMessageUi
import com.neochat.app.render.Block
import com.neochat.app.render.MarkdownParser

/**
 * 助手消息长按菜单能发起的意图（§3.5 ST35：先"本条消息"，再"会重开这一轮的"）。
 * 选择文字、引用、分享、朗读、从这里分叉不在 M1 范围，见交付报告的偏差清单。
 */
internal enum class MessageActionKind { COPY_ALL, COPY_CODE, REGENERATE, DELETE }

/** 一行菜单项；`codeIndex` 只对 [MessageActionKind.COPY_CODE] 有意义。 */
internal data class MessageAction(val kind: MessageActionKind, val codeIndex: Int = -1)

/**
 * 一条助手消息的菜单行。流式中这一轮还没落定，重新生成与软删都不给：
 * 前者会和在飞的请求抢同一层父节点，后者会让流事件写进一条已删除的消息。
 */
internal fun actionsFor(message: ChatMessageUi, codeBlockCount: Int): List<MessageAction> {
    val actions = mutableListOf<MessageAction>()
    if (message.text.isNotBlank()) {
        actions.add(MessageAction(MessageActionKind.COPY_ALL))
    }
    repeat(codeBlockCount) { index ->
        actions.add(MessageAction(MessageActionKind.COPY_CODE, index))
    }
    if (!message.streaming) {
        actions.add(MessageAction(MessageActionKind.REGENERATE))
        actions.add(MessageAction(MessageActionKind.DELETE))
    }
    return actions
}

/** 回复里的围栏代码块，按阅读顺序取源码原文（含嵌在列表 / 引用 / 脚注里的块）。 */
internal fun codeBlocksOf(markdown: String): List<String> {
    if (markdown.isBlank()) return emptyList()
    val found = mutableListOf<String>()
    collectCodeBlocks(MarkdownParser().parse(markdown).blocks, found)
    return found
}

private fun collectCodeBlocks(blocks: List<Block>, found: MutableList<String>) {
    blocks.forEach { block ->
        when (block) {
            is Block.Code -> found.add(block.text)
            is Block.ListBlock -> block.items.forEach { item -> collectCodeBlocks(item.blocks, found) }
            is Block.Quote -> collectCodeBlocks(block.blocks, found)
            is Block.FootnoteDef -> collectCodeBlocks(block.blocks, found)
            else -> Unit
        }
    }
}
