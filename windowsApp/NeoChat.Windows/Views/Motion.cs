using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using NeoChat.Windows.Core.Design;

namespace NeoChat.Windows.Views;

/// <summary>
/// Storyboards built from the motion tokens. Inline XAML durations would be magic numbers, so every
/// animation is constructed here instead.
/// </summary>
internal static class Motion
{
    /// <summary>ST01 光标呼吸：1.0 ↔ 0.25，周期 1000ms。</summary>
    internal static Storyboard CreateCursorBlink(DependencyObject target) => CreatePulse(
        target,
        Tokens.Motion.CursorOpacityHigh,
        Tokens.Motion.CursorOpacityLow,
        TimeSpan.FromSeconds(Tokens.Motion.CursorCycleSeconds));

    /// <summary>ST12 状态圆点呼吸：1.0 ↔ 0.35，周期 1200ms。</summary>
    internal static Storyboard CreateStatusDotBreath(DependencyObject target) => CreatePulse(
        target,
        Tokens.Motion.StatusDotOpacityHigh,
        Tokens.Motion.StatusDotOpacityLow,
        TimeSpan.FromSeconds(Tokens.Motion.StatusDotCycleSeconds));

    /// <summary>等待首个 token 的三点错峰（§5 差异 6：用三点替代 dsh-Go 的光球）。</summary>
    internal static Storyboard CreateWaitingDots(IReadOnlyList<UIElement> dots)
    {
        var storyboard = new Storyboard { BeginTime = TimeSpan.Zero };
        var cycle = TimeSpan.FromSeconds(Tokens.Motion.StatusDotCycleSeconds);
        for (var index = 0; index < dots.Count; index++)
        {
            var animation = new DoubleAnimationUsingKeyFrames
            {
                Duration = new Duration(cycle),
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromMilliseconds(Tokens.Motion.Quick.TotalMilliseconds * index / 2),
            };
            animation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = Tokens.Motion.CursorOpacityLow });
            animation.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromTicks(cycle.Ticks / 2)),
                Value = Tokens.Motion.CursorOpacityHigh,
            });
            animation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(cycle), Value = Tokens.Motion.CursorOpacityLow });
            Storyboard.SetTarget(animation, dots[index]);
            Storyboard.SetTargetProperty(animation, "Opacity");
            storyboard.Children.Add(animation);
        }

        return storyboard;
    }

    private static Storyboard CreatePulse(DependencyObject target, double high, double low, TimeSpan duration)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = new Duration(duration),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        animation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = high });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromTicks(duration.Ticks / 2)),
            Value = low,
        });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(duration), Value = high });

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        return storyboard;
    }
}
