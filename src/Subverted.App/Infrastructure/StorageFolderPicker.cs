using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Subverted.App.ViewModels;

namespace Subverted.App.Infrastructure;

/// <summary>The operating system's own folder dialog, parented to the window that asked.</summary>
/// <param name="owner">
/// Asked when the dialog opens rather than at construction: the window and its view model need
/// each other, and the window is the one that can be looked up later.
/// </param>
public sealed class StorageFolderPicker(Func<TopLevel?> owner) : IFolderPicker
{
    public async Task<string?> PickAsync(CancellationToken cancellationToken)
    {
        if (owner() is not { } topLevel)
        {
            return null;
        }

        var picked = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Open a working copy", AllowMultiple = false }
        );

        // A folder with no local path — a cloud provider's virtual one — cannot hold a checkout.
        return picked.Count == 0 ? null : picked[0].TryGetLocalPath();
    }
}
