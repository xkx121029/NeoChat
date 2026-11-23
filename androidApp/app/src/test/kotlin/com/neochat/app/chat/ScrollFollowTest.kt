package com.neochat.app.chat

import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/** 贴底状态机：只有"位置上移"能解除跟随（Y09 / Y10）。 */
class ScrollFollowTest {

    private val slack = 8f

    @Test
    fun `流式内容变高而视口不动时仍跟随`() {
        val follow = ScrollFollow(slack)

        follow.onScroll(position = 1_200f, distanceToBottom = 40f)
        assertTrue(follow.isFollowing)
        assertTrue(follow.onContentChanged())

        follow.onScroll(position = 1_200f, distanceToBottom = 160f)
        assertTrue(follow.isFollowing)
        assertTrue(follow.showJumpToBottom.not())
    }

    @Test
    fun `位置上移解除跟随并露出回到底部`() {
        val follow = ScrollFollow(slack)
        follow.onScroll(position = 5_000f, distanceToBottom = 4f)

        follow.onScroll(position = 4_000f, distanceToBottom = 600f)

        assertFalse(follow.isFollowing)
        assertTrue(follow.showJumpToBottom)
        assertFalse(follow.onContentChanged())
    }

    @Test
    fun `回到贴底范围自动恢复跟随`() {
        val follow = ScrollFollow(slack)
        follow.onScroll(position = 5_000f, distanceToBottom = 4f)
        follow.onScroll(position = 4_000f, distanceToBottom = 600f)

        follow.onScroll(position = 4_400f, distanceToBottom = 2f)

        assertTrue(follow.isFollowing)
        assertFalse(follow.showJumpToBottom)
    }

    @Test
    fun `点回到底部之后重新跟随`() {
        val follow = ScrollFollow(slack)
        follow.onScroll(position = 5_000f, distanceToBottom = 4f)
        follow.onScroll(position = 4_000f, distanceToBottom = 600f)

        follow.jumpToBottom()

        assertTrue(follow.isFollowing)
        assertTrue(follow.onContentChanged())
    }

    @Test
    fun `亚像素抖动不算用户上滚`() {
        val follow = ScrollFollow(slack)
        follow.onScroll(position = 1_000f, distanceToBottom = 500f)

        follow.onScroll(position = 999.7f, distanceToBottom = 520f)

        assertTrue(follow.isFollowing)
    }
}
