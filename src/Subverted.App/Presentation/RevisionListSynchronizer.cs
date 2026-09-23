namespace Subverted.App.Presentation;

/// <summary>
/// Brings the shown History list up to date by the fewest edits, so a page arriving at the bottom
/// or a search narrowing the list keeps the scroll position and leaves unchanged rows alone.
/// </summary>
public static class RevisionListSynchronizer
{
    /// <param name="shown">Mutated in place; afterwards it equals <paramref name="fresh"/>, in order.</param>
    /// <param name="fresh">The list as it should be, newest first and unique by revision.</param>
    public static void Apply(IList<RevisionListItem> shown, IReadOnlyList<RevisionListItem> fresh)
    {
        var wanted = fresh.Select(item => item.Row.Revision).ToHashSet();
        for (var index = shown.Count - 1; index >= 0; index--)
        {
            if (!wanted.Contains(shown[index].Row.Revision))
            {
                shown.RemoveAt(index);
            }
        }

        // Both lists are now newest first over the same or fewer revisions, so every row still
        // shown is either in place or missing; a missing one is inserted where it belongs.
        for (var index = 0; index < fresh.Count; index++)
        {
            var item = fresh[index];
            if (index < shown.Count && shown[index].Row.Revision == item.Row.Revision)
            {
                if (shown[index] != item)
                {
                    shown[index] = item;
                }

                continue;
            }

            shown.Insert(index, item);
        }
    }
}
