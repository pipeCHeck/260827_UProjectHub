using System.Windows;

namespace UProjectHub.App.Services;

public sealed class WpfClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Clipboard.SetText(text);
    }
}
