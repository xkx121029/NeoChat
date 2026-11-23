namespace NeoChat.Windows.Core.Design;

/// <summary>
/// Decides which palette is active and reports every transition once. The view layer feeds it the
/// system theme (at startup and again whenever the OS reports a change), so "follow the system" is
/// testable without a window: the same value twice is not a switch, a flip is exactly one event.
/// </summary>
public sealed class SystemThemeFollower
{
    /// <summary>Starts from a known state; the caller passes what the system reports at launch.</summary>
    public SystemThemeFollower(bool systemIsDark) => IsDark = systemIsDark;

    public bool IsDark { get; private set; }

    public AppTheme Theme => IsDark ? AppTheme.Dark : AppTheme.Light;

    /// <summary>Raised only when the theme actually changed, so repainting is not done twice.</summary>
    public event EventHandler<AppTheme>? Changed;

    /// <summary>Returns true when this report was a switch (and therefore raised <see cref="Changed"/>).</summary>
    public bool Update(bool systemIsDark)
    {
        if (systemIsDark == IsDark)
        {
            return false;
        }

        IsDark = systemIsDark;
        Changed?.Invoke(this, Theme);
        return true;
    }
}
