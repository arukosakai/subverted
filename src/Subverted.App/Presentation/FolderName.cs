namespace Subverted.App.Presentation;

/// <summary>How a working copy is called on screen: its folder's own name, not its whole path.</summary>
public static class FolderName
{
    /// <returns>The last segment, or the path itself for a drive root where there is none.</returns>
    public static string Of(string path) =>
        Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            is { Length: > 0 } name
            ? name
            : path;
}
