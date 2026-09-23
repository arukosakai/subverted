namespace Subverted.App.Presentation;

/// <summary>
/// How a rename row says where the file came from. A rename within one folder reads as the old
/// name alone, since the folder is already beside it; a move between folders says the whole path.
/// </summary>
public static class RenameCaption
{
    /// <param name="fromRelPath">The old path, slash-separated as the daemon sends it.</param>
    /// <param name="toRelPath">The new path, in the same spelling.</param>
    public static string For(string fromRelPath, string toRelPath)
    {
        var sameFolder = FolderOf(fromRelPath) == FolderOf(toRelPath);
        var shown = sameFolder ? fromRelPath[(fromRelPath.LastIndexOf('/') + 1)..] : fromRelPath;
        return $"← {shown}";
    }

    private static string FolderOf(string relPath)
    {
        var separator = relPath.LastIndexOf('/');
        return separator < 0 ? string.Empty : relPath[..separator];
    }
}
