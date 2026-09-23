namespace Subverted.App.Presentation;

/// <summary>The default layout: one line per change, each saying which folder it is in.</summary>
public static class FlatChangeList
{
    /// <param name="rows">Already in the order they should show; see <see cref="ChangeOrder"/>.</param>
    public static IReadOnlyList<ChangeListItem> Of(IEnumerable<ChangeRow> rows) =>
        [.. rows.Select(ChangeListItem.Flat)];
}
