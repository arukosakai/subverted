namespace Subverted.App.Presentation;

/// <summary>
/// The directory pane: every folder that holds a change, in tree order, and which changes choosing
/// one keeps. Each is drawn in the most urgent tone below it, so a conflict three levels down still
/// colours the root. A rename belongs to the folders of both its names, as the filter finds it by either.
/// </summary>
public static class ChangeFolders
{
    /// <param name="rootName">What the root's line is called: the working copy's own folder name.</param>
    /// <returns>
    /// The root first, then each folder straight after its parent, siblings by name ignoring case;
    /// empty when there are no changes, so a clean copy shows no tree at all.
    /// </returns>
    public static IReadOnlyList<FolderLine> Of(IReadOnlyList<ChangeRow> rows, string rootName)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var folders = new HashSet<string>(StringComparer.Ordinal) { "" };
        foreach (var path in rows.SelectMany(PathsOf))
        {
            foreach (var folder in AncestorsOf(path))
            {
                folders.Add(folder);
            }
        }

        var branches = folders
            .Where(folder => folder.Length > 0)
            .Select(ParentOf)
            .ToHashSet(StringComparer.Ordinal);
        return
        [
            .. folders
                .Order(TreeOrder.Instance)
                .Select(folder => LineOf(folder, rows, rootName, branches.Contains(folder))),
        ];
    }

    private static FolderLine LineOf(
        string folder,
        IReadOnlyList<ChangeRow> rows,
        string rootName,
        bool hasSubfolders
    )
    {
        var held = rows.Where(row => Contains(folder, row)).ToList();
        return new FolderLine(
            folder,
            folder.Length == 0 ? rootName : NameOf(folder),
            DepthOf(folder),
            held.Count,
            ChangeUrgency.MostUrgentOf(held.Select(row => row.Badge.Tone)),
            hasSubfolders
        );
    }

    private static string ParentOf(string folder) =>
        folder.LastIndexOf('/') is var slash and >= 0 ? folder[..slash] : "";

    /// <summary>Whether choosing <paramref name="folder"/> keeps the row: the root keeps everything.</summary>
    public static bool Contains(string folder, ChangeRow row) =>
        PathsOf(row).Any(path => IsWithin(folder, path));

    private static bool IsWithin(string folder, string path) =>
        folder.Length == 0
        || path == folder
        || (path.StartsWith(folder, StringComparison.Ordinal) && path[folder.Length] == '/');

    private static IEnumerable<string> PathsOf(ChangeRow row) =>
        row.RenamedFrom is { } from ? [row.RelPath, from] : [row.RelPath];

    /// <summary>The folders above a path, not the path itself: a changed folder is a row, not a branch.</summary>
    private static IEnumerable<string> AncestorsOf(string path)
    {
        for (var slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
        {
            yield return path[..slash];
        }
    }

    private static string NameOf(string folder) => folder[(folder.LastIndexOf('/') + 1)..];

    private static int DepthOf(string folder) =>
        folder.Length == 0 ? 0 : folder.Count(character => character == '/') + 1;

    /// <summary>Segment by segment, so a folder sorts before its children and after its elder sibling's.</summary>
    private sealed class TreeOrder : IComparer<string>
    {
        public static readonly TreeOrder Instance = new();

        public int Compare(string? left, string? right)
        {
            var leftSegments = Segments(left!);
            var rightSegments = Segments(right!);
            for (
                var index = 0;
                index < Math.Min(leftSegments.Length, rightSegments.Length);
                index++
            )
            {
                var byName = StringComparer.OrdinalIgnoreCase.Compare(
                    leftSegments[index],
                    rightSegments[index]
                );
                var order =
                    byName != 0
                        ? byName
                        : StringComparer.Ordinal.Compare(leftSegments[index], rightSegments[index]);
                if (order != 0)
                {
                    return order;
                }
            }

            return leftSegments.Length.CompareTo(rightSegments.Length);
        }

        private static string[] Segments(string folder) =>
            folder.Length == 0 ? [] : folder.Split('/');
    }
}
