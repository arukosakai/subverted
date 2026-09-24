namespace Subverted.App.Presentation;

/// <summary>
/// The header's one-glance answer. Ordered by what needs a person first — a conflict before an
/// edit — and only tones that occur, so a clean copy summarises to nothing at all.
/// </summary>
public static class ChangeSummary
{
    public static IReadOnlyList<ChangeCount> Of(IEnumerable<ChangeRow> rows)
    {
        var counts = rows.GroupBy(row => row.Badge.Tone)
            .ToDictionary(group => group.Key, group => group.Count());

        return
        [
            .. ChangeUrgency
                .Order.Where(counts.ContainsKey)
                .Select(tone => new ChangeCount(tone, counts[tone], Text(tone, counts[tone]))),
        ];
    }

    private static string Text(ChangeTone tone, int count) =>
        tone switch
        {
            ChangeTone.Conflict => count == 1 ? "1 conflict" : $"{count} conflicts",
            ChangeTone.Missing => $"{count} missing",
            ChangeTone.Modified => $"{count} modified",
            ChangeTone.Added => $"{count} added",
            ChangeTone.Deleted => $"{count} deleted",
            ChangeTone.Replaced => $"{count} replaced",
            ChangeTone.Renamed => $"{count} renamed",
            _ => $"{count} not versioned",
        };
}
