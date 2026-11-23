using Microsoft.UI.Xaml;
using NeoChat.Windows.Design;
using NeoChat.Windows.Views;

namespace NeoChat.Windows;

public partial class App : Application
{
    private static readonly string TracePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NeoChat",
        "startup.log");

    private Window? _window;

    public App()
    {
        Trace("App ctor: start");
        UnhandledException += (_, args) => Trace("unhandled", args.Exception);
        InitializeComponent();
        Trace("App ctor: done");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // 全部设计令牌由 Design/Tokens.cs 生成，视图只按 key 引用（无字面颜色、无魔法数字）。
            // 必须在 OnLaunched 里合并：在 App 构造函数里读 Application.Resources 会抛 0x8000FFFF。
            var tokens = TokenResources.Build();
            Resources.MergedDictionaries.Add(tokens);
            Trace("OnLaunched: tokens merged (" + Resources.MergedDictionaries.Count + " dictionaries)");

            // 跟随系统主题：启动时读 ApplicationTheme，运行中听 UISettings 的颜色变化（见 Design/ThemeRuntime.cs）。
            ThemeRuntime.Start(tokens);
            Trace("OnLaunched: theme " + ThemeRuntime.Current);
            Trace("OnLaunched: building MainWindow");
            _window = new MainWindow();
            Trace("OnLaunched: window created");
            _window.Activate();
            Trace("OnLaunched: window activated");
        }
        catch (Exception exception)
        {
            Trace("OnLaunched failed", exception);
            throw;
        }
    }

    /// <summary>
    /// Unpackaged M1 has no crash reporting yet, so startup problems are appended to
    /// <c>%LOCALAPPDATA%\NeoChat\startup.log</c>; a missing resource key otherwise dies silently.
    /// </summary>
    internal static void Trace(string message, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TracePath)!);
            var line = DateTimeOffset.Now.ToString("O") + " " + message;
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            File.AppendAllText(TracePath, line + Environment.NewLine);
        }
        catch (IOException)
        {
            // A trace that cannot be written must not take the app down with it.
        }
    }
}
