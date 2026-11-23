using Windows.ApplicationModel.DataTransfer;

namespace NeoChat.Windows.Views;

/// <summary>One place that puts text on the Windows clipboard.</summary>
internal static class ClipboardWriter
{
    internal static void Write(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
