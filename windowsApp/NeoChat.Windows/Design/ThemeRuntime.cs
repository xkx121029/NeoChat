using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NeoChat.Windows.Core.Design;
using Windows.UI.ViewManagement;

namespace NeoChat.Windows.Design;

/// <summary>
/// Makes the app follow the system theme, including a switch made while it is running.
/// <para>
/// <see cref="Application.RequestedTheme"/> is the theme the process launched under — it does not move
/// during a session — so it is read once for the starting palette, and the live signal is
/// <see cref="UISettings.ColorValuesChanged"/>, which Windows raises when the user flips the system
/// light/dark setting. That event arrives on a background thread and is marshalled to the UI thread
/// before anything is repainted.
/// </para>
/// </summary>
internal static class ThemeRuntime
{
    private static SystemThemeFollower? _follower;
    private static ResourceDictionary? _tokens;
    private static UISettings? _settings;
    private static DispatcherQueue? _dispatcher;

    /// <summary>Raised on the UI thread after the brushes were repainted.</summary>
    internal static event EventHandler? Changed;

    internal static AppTheme Current => ThemePainter.Theme;

    internal static void Start(ResourceDictionary tokens)
    {
        _tokens = tokens;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _follower = new SystemThemeFollower(Application.Current.RequestedTheme == ApplicationTheme.Dark);
        _follower.Changed += OnFollowed;
        ThemePainter.Paint(tokens, _follower.Theme);

        _settings = new UISettings();
        _settings.ColorValuesChanged += OnSystemColorsChanged;
    }

    private static void OnSystemColorsChanged(UISettings sender, object args) =>
        _dispatcher?.TryEnqueue(() => _follower?.Update(IsDark(sender)));

    /// <summary>UISettings reports the window background: near-black means the dark half of the palette.</summary>
    private static bool IsDark(UISettings settings)
    {
        var background = settings.GetColorValue(UIColorType.Background);
        return (background.R + background.G + background.B) / 3 < ThemePalette.DarkLuminanceSplit;
    }

    private static void OnFollowed(object? sender, AppTheme theme)
    {
        if (_tokens is not null)
        {
            ThemePainter.Paint(_tokens, theme);
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }
}
