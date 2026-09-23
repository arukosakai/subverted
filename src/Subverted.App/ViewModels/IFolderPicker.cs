namespace Subverted.App.ViewModels;

/// <summary>Asks the person for a folder, the way their operating system asks.</summary>
public interface IFolderPicker
{
    /// <returns>The chosen folder's local path, or null when they cancelled.</returns>
    Task<string?> PickAsync(CancellationToken cancellationToken);
}
