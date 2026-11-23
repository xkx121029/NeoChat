package com.neochat.app.chat

/**
 * 消息列表是否继续贴底（Y09 / Y10）。
 *
 * 只有**位置上移**能解除跟随。流式内容变高会同时抬高滚动范围，如果把"范围变化"当成
 * "用户上滚"，跟随会在生成中途自己跑掉；所以这里只比较滚动位置本身。
 */
public class ScrollFollow(
    private val slack: Float,
    private val movedUpTolerance: Float = MOVED_UP_TOLERANCE,
) {

    /** 当前是否贴底；新内容是否要自动滚动以它为准。 */
    public var isFollowing: Boolean = true
        private set

    private var lastPosition: Float? = null

    /** Y09 的显隐：不再跟随才给"回到底部"。 */
    public val showJumpToBottom: Boolean
        get() = !isFollowing

    /**
     * 一次滚动位置上报。`position` 随列表向下增大（顶部为 0，等于
     * `firstVisibleItemIndex * 跨度 + firstVisibleItemScrollOffset`），`distanceToBottom`
     * 是最后一个可见项底部到视口底边的距离，可为负（内容短于视口）。
     */
    public fun onScroll(position: Float, distanceToBottom: Float) {
        val previous = lastPosition
        lastPosition = position
        if (distanceToBottom <= slack) {
            isFollowing = true
            return
        }
        if (previous != null && position < previous - movedUpTolerance) {
            isFollowing = false
        }
    }

    /** 新内容到达（token、换行、尺寸变化）：跟随时才需要贴底。 */
    public fun onContentChanged(): Boolean = isFollowing

    /** 点了"回到底部"：从这里开始重新跟随。 */
    public fun jumpToBottom() {
        isFollowing = true
    }

    public companion object {
        /** 推断值：亚像素抖动不算"用户上滚"，取半个 dp 的像素量。 */
        public const val MOVED_UP_TOLERANCE: Float = 0.5f

        /** 结构性常量（非设计值）：把 `(index, offset)` 压成一个单调位置时，单条消息的跨度。 */
        public const val POSITION_ITEM_SPAN: Long = 1_000_000L
    }
}
