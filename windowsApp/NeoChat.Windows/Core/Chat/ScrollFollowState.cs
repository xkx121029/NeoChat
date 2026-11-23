namespace NeoChat.Windows.Core.Chat;

/// <summary>
/// Whether the message list should stay glued to the newest token (Y10).
/// <para>
/// The rule the view used to hold in a single boolean was wrong in one specific way: growing content
/// raises the scroll extent, and an extent change was read as "the user scrolled away", so following
/// stopped by itself mid-stream. Here only an <em>upward</em> move of the offset can unpin the list;
/// content that grows under a stationary viewport cannot.
/// </para>
/// </summary>
public sealed class ScrollFollowState
{
    /// <summary>推断值：亚像素抖动不足以算作"用户上滚"，取半个 DIP。</summary>
    public const double MovedUpTolerance = 0.5;

    private readonly double _threshold;
    private double _lastOffset;

    /// <summary><paramref name="threshold"/> is Y10 的最小空档（<c>Tokens.Layout.FollowThreshold</c>）。</summary>
    public ScrollFollowState(double threshold) => _threshold = threshold;

    public bool IsFollowing { get; private set; } = true;

    /// <summary>Y09 的显隐：不再跟随才给"回到底部"。</summary>
    public bool ShowJumpToBottom => !IsFollowing;

    /// <summary>
    /// One scroll-position report from the viewer. A bigger <paramref name="verticalOffset"/> is a move
    /// down; only a move up past the slack can stop following.
    /// </summary>
    public void OffsetChanged(double verticalOffset, double distanceToBottom)
    {
        var movedUp = verticalOffset < _lastOffset - MovedUpTolerance;
        _lastOffset = verticalOffset;

        if (distanceToBottom <= _threshold)
        {
            IsFollowing = true;
            return;
        }

        if (movedUp)
        {
            IsFollowing = false;
        }
    }

    /// <summary>New content arrived (token, row, resize): scroll to the bottom only while following.</summary>
    public bool ContentChanged() => IsFollowing;

    /// <summary>The "back to bottom" key was pressed: follow again from here on.</summary>
    public void JumpToBottom() => IsFollowing = true;
}
