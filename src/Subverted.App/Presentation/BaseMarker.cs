using Subverted.Core;

namespace Subverted.App.Presentation;

/// <summary>Where the "your copy is here" line sits in the History list, and what it says.</summary>
public static class BaseMarker
{
    /// <param name="shown">What the list shows, newest first — after search, so the line moves with it.</param>
    /// <param name="range">This working copy's BASE range, or null while it is unknown.</param>
    /// <returns>
    /// One item per row. The line goes above the newest row at or below the highest BASE, and on
    /// no row when every shown row is newer, because the copy's place is further down the history.
    /// </returns>
    public static IReadOnlyList<RevisionListItem> Place(
        IReadOnlyList<RevisionRow> shown,
        BaseRevisionRange? range
    )
    {
        if (range is null)
        {
            return [.. shown.Select(row => new RevisionListItem(row, null, null))];
        }

        var items = new List<RevisionListItem>(shown.Count);
        var label = Describe(range);
        var isPlaced = false;
        foreach (var row in shown)
        {
            var presence = PresenceOf(row.Revision, range);
            var isMarkerAbove = !isPlaced && presence != RevisionPresence.NotInCopy;
            isPlaced |= isMarkerAbove;
            items.Add(new RevisionListItem(row, presence, isMarkerAbove ? label : null));
        }

        return items;
    }

    public static RevisionPresence PresenceOf(long revision, BaseRevisionRange range) =>
        revision > range.Highest ? RevisionPresence.NotInCopy
        : revision > range.Lowest ? RevisionPresence.PartlyInCopy
        : RevisionPresence.InCopy;

    /// <summary>The line's label, which says so when there is no single BASE to name.</summary>
    public static string Describe(BaseRevisionRange range) =>
        range.IsMixed
            ? $"Your copy is at r{range.Lowest}–r{range.Highest} — mixed, updated in parts"
            : $"Your copy is at r{range.Highest}";
}
