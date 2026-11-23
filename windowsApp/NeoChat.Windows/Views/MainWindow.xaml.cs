using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using NeoChat.Windows.Core.Design;
using NeoChat.Windows.Design;

namespace NeoChat.Windows.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "NeoChat";

        // 标题栏是系统画的，不是页面画的：页面换底色后必须把这一块也按令牌上色，否则暗色下会留一条亮边。
        ApplyTitleBarTheme();
        ThemeRuntime.Changed += (_, _) => ApplyTitleBarTheme();
    }

    private void ApplyTitleBarTheme()
    {
        var titleBar = AppWindow.TitleBar;
        titleBar.PreferredTheme = ThemeRuntime.Current == AppTheme.Dark ? TitleBarTheme.Dark : TitleBarTheme.Light;
        titleBar.ButtonBackgroundColor = ThemePainter.Argb(Tokens.Palette.Transparent);
        titleBar.ButtonInactiveBackgroundColor = ThemePainter.Argb(Tokens.Palette.Transparent);
        titleBar.ButtonHoverBackgroundColor = ThemePainter.Tone("PillFill");
        titleBar.ButtonPressedBackgroundColor = ThemePainter.Tone("SlateContainer");
        titleBar.ButtonForegroundColor = ThemePainter.Tone("InkMuted");
        titleBar.ButtonInactiveForegroundColor = ThemePainter.Tone("InkGhost");
        titleBar.ButtonHoverForegroundColor = ThemePainter.Tone("Ink");
        titleBar.ButtonPressedForegroundColor = ThemePainter.Tone("Ink");
    }
}
