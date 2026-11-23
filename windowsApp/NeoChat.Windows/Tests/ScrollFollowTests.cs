using NeoChat.Windows.Core.Chat;
using NeoChat.Windows.Core.Design;
using Xunit;

namespace NeoChat.Windows.Core.Tests;

/// <summary>
/// 自动滚动状态机：流式时贴底跟随，用户上滚后停手并露出"回到底部"，按下它恢复跟随。
/// 这些断言直接跑视图使用的那份状态（<see cref="ScrollFollowState"/>），不是对视图的间接猜测。
/// </summary>
public sealed class ScrollFollowTests
{
    private static ScrollFollowState State() => new(Tokens.Layout.FollowThreshold);

    [Fact]
    public void StreamingKeepsFollowingWhileTheReaderSitsAtTheBottom()
    {
        var state = State();

        state.OffsetChanged(0, 0);
        Assert.True(state.ContentChanged());

        // 内容长高、视口没动：位置没上去，不许被当成"用户滚走了"。
        state.OffsetChanged(0, 640);
        Assert.True(state.ContentChanged());
        Assert.False(state.ShowJumpToBottom);

        state.OffsetChanged(64, 576);
        Assert.True(state.ContentChanged());
    }

    [Fact]
    public void UserScrollingUpDuringStreamingStopsFollowingAndTheKeyRestoresIt()
    {
        var state = State();
        state.OffsetChanged(1000, 0);
        Assert.True(state.ContentChanged());

        // 流式中用户上滚：停止跟随，露出回到底部。
        state.OffsetChanged(600, 1040);
        Assert.False(state.ContentChanged());
        Assert.True(state.ShowJumpToBottom);

        // 继续流式变高，位置不动：仍然不许抢回滚动条。
        state.OffsetChanged(600, 1240);
        Assert.False(state.ContentChanged());
        Assert.True(state.ShowJumpToBottom);

        // 点"回到底部"：恢复跟随，按钮收起。
        state.JumpToBottom();
        Assert.True(state.ContentChanged());
        Assert.False(state.ShowJumpToBottom);
    }

    [Fact]
    public void ScrollingBackDownByHandAlsoResumesFollowing()
    {
        var state = State();
        state.OffsetChanged(800, 0);
        state.OffsetChanged(200, 1200);
        Assert.False(state.ContentChanged());

        // 用户自己滑回底部（Y10 的 8px 空档内）。
        state.OffsetChanged(1196, 4);
        Assert.True(state.ContentChanged());
        Assert.False(state.ShowJumpToBottom);
    }

    [Fact]
    public void ASmallWheelNudgeInsideTheSlackKeepsFollowing()
    {
        var state = State();
        state.OffsetChanged(1000, 0);

        // 往上 4px 仍在 8px 空档内：不算离开底部，也不该弹按钮。
        state.OffsetChanged(996, 4);
        Assert.True(state.ContentChanged());
        Assert.False(state.ShowJumpToBottom);
    }
}
