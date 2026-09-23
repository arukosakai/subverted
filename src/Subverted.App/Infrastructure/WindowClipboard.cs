using Avalonia.Controls;
using Avalonia.Input.Platform;
using Subverted.App.ViewModels;

namespace Subverted.App.Infrastructure;

/// <summary>The system clipboard, through the window's.</summary>
/// <param name="owner">Asked when copying, for the same reason <see cref="StorageFolderPicker"/> asks.</param>
public sealed class WindowClipboard(Func<TopLevel?> owner) : ITextClipboard
{
    public async Task CopyAsync(string text)
    {
        if (owner()?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
