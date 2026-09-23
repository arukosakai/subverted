namespace Subverted.App.Presentation;

/// <summary>
/// The tree layout: changes nested under their folders, with a line for every folder on the way
/// down. Pinned rows stay above the tree as flat lines, since a conflict must not sit three levels
/// deep in a folder nobody has opened.
/// </summary>
public static class ChangeTree
{
    /// <param name="rows">In the order pinned rows should show; the tree itself is ordered by path.</param>
    public static IReadOnlyList<ChangeListItem> Of(IEnumerable<ChangeRow> rows)
    {
        var all = rows.ToList();
        var pinned = all.Where(ChangeOrder.IsPinned).Select(ChangeListItem.Flat);
        var nested = Nest(all.Where(row => !ChangeOrder.IsPinned(row)));
        return [.. pinned, .. nested];
    }

    private static IEnumerable<ChangeListItem> Nest(IEnumerable<ChangeRow> rows)
    {
        var nodes = new SortedDictionary<string, ChangeRow?>(SegmentOrder.Instance);
        foreach (var row in rows)
        {
            nodes[row.RelPath] = row;
            foreach (var folder in AncestorsOf(row.RelPath))
            {
                nodes.TryAdd(folder, null);
            }
        }

        return nodes.Select(node => Item(node.Key, node.Value));
    }

    private static ChangeListItem Item(string path, ChangeRow? row)
    {
        var depth = path.Count(character => character == '/');
        return row is null
            ? new ChangeListItem(path, path[(path.LastIndexOf('/') + 1)..], "", depth, null)
            : new ChangeListItem(path, row.Name, "", depth, row);
    }

    private static IEnumerable<string> AncestorsOf(string relPath)
    {
        for (var slash = relPath.IndexOf('/'); slash >= 0; slash = relPath.IndexOf('/', slash + 1))
        {
            yield return relPath[..slash];
        }
    }

    /// <summary>
    /// Segment by segment, so a folder's contents sit directly under it: plain ordinal order puts
    /// <c>art-old</c> between <c>art</c> and <c>art/hero.png</c>, because '-' sorts before '/'.
    /// </summary>
    private sealed class SegmentOrder : IComparer<string>
    {
        public static readonly SegmentOrder Instance = new();

        public int Compare(string? x, string? y) =>
            string.CompareOrdinal(x!.Replace('/', '\0'), y!.Replace('/', '\0'));
    }
}
