namespace Subverted.App.Presentation;

/// <summary>
/// What the filter box keeps: rows whose path — or, for a rename, old path — contains the typed
/// text, ignoring case on every platform: it is a search, not a path comparison. Blank text keeps
/// everything.
/// </summary>
public static class ChangeFilter
{
    public static bool Keeps(ChangeRow row, string text)
    {
        var wanted = text.Trim();
        return wanted.Length == 0
            || row.RelPath.Contains(wanted, StringComparison.OrdinalIgnoreCase)
            || row.RenamedFrom?.Contains(wanted, StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <returns>The kept rows, in the order they came.</returns>
    public static IReadOnlyList<ChangeRow> Apply(IEnumerable<ChangeRow> rows, string text) =>
        [.. rows.Where(row => Keeps(row, text))];
}
