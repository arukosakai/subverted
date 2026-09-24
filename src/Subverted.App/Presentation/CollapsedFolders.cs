namespace Subverted.App.Presentation;

/// <summary>
/// Which folders of the directory pane the person collapsed, kept by path so the resync — which
/// rebuilds every line — leaves them collapsed. Everything starts expanded.
/// </summary>
public sealed class CollapsedFolders
{
    private readonly HashSet<string> _paths = new(StringComparer.Ordinal);

    public IReadOnlySet<string> Paths => _paths;

    public void Collapse(string relPath) => _paths.Add(relPath);

    public void Expand(string relPath) => _paths.Remove(relPath);

    /// <summary>
    /// Forgets every folder the fresh tree no longer has a branch for, so one that empties and
    /// later comes back returns expanded, like any folder seen for the first time.
    /// </summary>
    public void Follow(IEnumerable<FolderLine> tree) =>
        _paths.IntersectWith(tree.Where(line => line.HasSubfolders).Select(line => line.RelPath));
}
