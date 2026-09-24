namespace Subverted.App.Presentation;

/// <summary>
/// The directory pane with some folders collapsed: which lines are left to show, and where a
/// selection hidden by a collapse lands instead.
/// </summary>
public static class FolderOutline
{
    /// <param name="tree">Every folder line, as <see cref="ChangeFolders.Of"/> gives them.</param>
    /// <param name="collapsed">RelPaths of the collapsed folders.</param>
    /// <returns>
    /// The tree in its own order without any line below a collapsed folder, each collapsed one
    /// marked <see cref="FolderLine.IsCollapsed"/>. A folder with nothing under it never is.
    /// </returns>
    public static IReadOnlyList<FolderLine> Shown(
        IReadOnlyList<FolderLine> tree,
        IReadOnlySet<string> collapsed
    ) =>
        [
            .. tree.Where(line => !collapsed.Any(folder => IsBelow(line.RelPath, folder)))
                .Select(line =>
                    line with
                    {
                        IsCollapsed = line.HasSubfolders && collapsed.Contains(line.RelPath),
                    }
                ),
        ];

    /// <returns>
    /// The outermost collapsed folder above <paramref name="selected"/>, which is the line still
    /// showing where it was; <paramref name="selected"/> itself when nothing above it is collapsed.
    /// </returns>
    public static string Landing(string selected, IReadOnlySet<string> collapsed) =>
        collapsed
            .Where(folder => IsBelow(selected, folder))
            .OrderBy(folder => folder.Length)
            .FirstOrDefault()
        ?? selected;

    /// <summary>Strictly below: a folder is not below itself, and everything but the root is below the root.</summary>
    private static bool IsBelow(string path, string folder) =>
        folder.Length == 0
            ? path.Length > 0
            : path.Length > folder.Length
                && path[folder.Length] == '/'
                && path.StartsWith(folder, StringComparison.Ordinal);
}
