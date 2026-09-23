namespace Subverted.App.Presentation;

/// <summary>
/// The order the list shows changes in: what is broken first — conflicts, then anything missing
/// from disk, the order <see cref="ChangeSummary"/> counts them in — and everything else by path.
/// </summary>
public static class ChangeOrder
{
    /// <summary>Whether a row is held at the top of the list whatever the layout.</summary>
    public static bool IsPinned(ChangeRow row) => Rank(row) < Unpinned;

    /// <returns>A new list; ties within a rank break by ordinal path, so the order is total.</returns>
    public static IReadOnlyList<ChangeRow> Of(IEnumerable<ChangeRow> rows) =>
        [.. rows.OrderBy(Rank).ThenBy(row => row.RelPath, StringComparer.Ordinal)];

    private const int Unpinned = 2;

    private static int Rank(ChangeRow row) =>
        row.Badge.Tone switch
        {
            ChangeTone.Conflict => 0,
            ChangeTone.Missing => 1,
            _ => Unpinned,
        };
}
