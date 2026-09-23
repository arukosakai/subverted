using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Subverted.App.ViewModels;

namespace Subverted.App.Infrastructure;

/// <summary>The operating system's own "open with", through the window's launcher.</summary>
/// <param name="owner">Asked when opening, for the same reason <see cref="StorageFolderPicker"/> asks.</param>
public sealed class SystemFileLauncher(Func<TopLevel?> owner) : IFileLauncher
{
    public async Task OpenAsync(string path)
    {
        if (owner() is { } topLevel)
        {
            await topLevel.Launcher.LaunchFileInfoAsync(new FileInfo(path));
        }
    }
}
