namespace Subverted.App.Presentation;

/// <summary>
/// Brings a displayed list up to date with a fresh one by the fewest edits. A live list replaced
/// wholesale every second flickers, loses its scroll position and drops the selection; one edited
/// in place moves only the rows that changed.
/// </summary>
public static class ChangeListSynchronizer
{
    /// <param name="shown">Mutated in place; afterwards it equals <paramref name="fresh"/>, in order.</param>
    /// <param name="fresh">The new listing, ordered as it should be shown and unique by path.</param>
    public static void Apply(IList<ChangeRow> shown, IReadOnlyList<ChangeRow> fresh)
    {
        var wanted = fresh.Select(row => row.RelPath).ToHashSet(StringComparer.Ordinal);
        for (var index = shown.Count - 1; index >= 0; index--)
        {
            if (!wanted.Contains(shown[index].RelPath))
            {
                shown.RemoveAt(index);
            }
        }

        for (var index = 0; index < fresh.Count; index++)
        {
            var row = fresh[index];
            if (index < shown.Count && shown[index].RelPath == row.RelPath)
            {
                // Equal records are left alone, so an unchanged row keeps its container and selection.
                if (shown[index] != row)
                {
                    shown[index] = row;
                }

                continue;
            }

            var existing = IndexOf(shown, row.RelPath, from: index);
            if (existing >= 0)
            {
                shown.RemoveAt(existing);
            }

            shown.Insert(index, row);
        }
    }

    private static int IndexOf(IList<ChangeRow> rows, string relPath, int from)
    {
        for (var index = from; index < rows.Count; index++)
        {
            if (rows[index].RelPath == relPath)
            {
                return index;
            }
        }

        return -1;
    }
}
