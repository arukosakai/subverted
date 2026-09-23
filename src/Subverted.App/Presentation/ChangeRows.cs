using Subverted.Core;

namespace Subverted.App.Presentation;

/// <summary>
/// The rows a listing shows: one per entry, except that a rename made outside SVN — which the
/// daemon lists as an unrelated missing node and unversioned file — becomes one rename row.
/// </summary>
public static class ChangeRows
{
    /// <param name="entries">The listing, in any order.</param>
    /// <param name="moves">
    /// The pairs D27 found. A pair is shown only when both halves are listed: a status scoped to a
    /// subfolder can hold one half, and a half on its own is exactly what SVN sees.
    /// </param>
    /// <returns>In the listing's order, each rename row where its new path's entry was.</returns>
    public static IReadOnlyList<ChangeRow> From(
        IReadOnlyList<WorkingCopyEntry> entries,
        IReadOnlyList<UnrecordedMove> moves
    )
    {
        var listed = entries.Select(entry => entry.RelPath).ToHashSet(StringComparer.Ordinal);
        var fromByDestination = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var move in moves)
        {
            if (listed.Contains(move.FromRelPath) && listed.Contains(move.ToRelPath))
            {
                fromByDestination.TryAdd(move.ToRelPath, move.FromRelPath);
            }
        }

        var folded = fromByDestination.Values.ToHashSet(StringComparer.Ordinal);

        return
        [
            .. entries
                .Where(entry => !folded.Contains(entry.RelPath))
                .Select(entry =>
                    fromByDestination.TryGetValue(entry.RelPath, out var from)
                        ? ChangeRow.Rename(entry, from)
                        : ChangeRow.From(entry)
                ),
        ];
    }
}
