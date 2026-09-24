namespace Subverted.App.Presentation;

/// <summary>
/// The directory pane: every folder that holds a listed row, in tree order, and which rows choosing
/// one keeps. Each is drawn in the most urgent tone below it, so a conflict three levels down still
/// colours the root. A rename belongs to the folders of both its names, as the filter finds it by either.
/// </summary>
public static class ChangeFolders
{
    /// <param name="rows">
    /// Every listed row. One with nothing to report (<see cref="ChangeRow.IsUnmodified"/>) puts its
    /// folders in the tree but is neither counted nor coloured: the pane's numbers are about changes.
    /// </param>
    /// <param name="rootName">What the root's line is called: the working copy's own folder name.</param>
    /// <returns>
    /// The root first, then each folder straight after its parent, siblings by name ignoring case;
    /// empty when nothing is listed, so a clean copy shows no tree at all.
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

        var held = TonesHeld(rows, folders);
        var branches = folders
            .Where(folder => folder.Length > 0)
            .Select(ParentOf)
            .ToHashSet(StringComparer.Ordinal);
        return
        [
            .. folders
                .Order(TreeOrder.Instance)
                .Select(folder =>
                    LineOf(folder, held[folder], rootName, branches.Contains(folder))
                ),
        ];
    }

    /// <summary>
    /// The tone of every change each folder holds, in one pass over the rows rather than one per
    /// folder — on an everything-listing of a large copy, milliseconds instead of seconds.
    /// </summary>
    private static Dictionary<string, List<ChangeTone>> TonesHeld(
        IReadOnlyList<ChangeRow> rows,
        HashSet<string> folders
    )
    {
        var held = folders.ToDictionary(
            folder => folder,
            _ => new List<ChangeTone>(),
            StringComparer.Ordinal
        );
        foreach (var row in rows.Where(row => !row.IsUnmodified))
        {
            // A set, so a rename with both names in one folder counts there once.
            var holders = PathsOf(row)
                .SelectMany(path => AncestorsOf(path).Append(path))
                .Where(folders.Contains)
                .Append("")
                .ToHashSet(StringComparer.Ordinal);
            foreach (var folder in holders)
            {
                held[folder].Add(row.Badge.Tone);
            }
        }

        return held;
    }

    private static FolderLine LineOf(
        string folder,
        List<ChangeTone> held,
        string rootName,
        bool hasSubfolders
    ) =>
        new(
            folder,
            folder.Length == 0 ? rootName : NameOf(folder),
            DepthOf(folder),
            held.Count,
            ChangeUrgency.MostUrgentOf(held),
            hasSubfolders
        );

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
