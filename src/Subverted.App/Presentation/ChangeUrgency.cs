namespace Subverted.App.Presentation;

/// <summary>
/// Which tone needs a person first: a conflict before anything missing, both before an edit.
/// <see cref="ChangeTone.Quiet"/> is not in the order, as nothing about it needs anyone.
/// </summary>
public static class ChangeUrgency
{
    public static readonly IReadOnlyList<ChangeTone> Order =
    [
        ChangeTone.Conflict,
        ChangeTone.Missing,
        ChangeTone.Modified,
        ChangeTone.Added,
        ChangeTone.Deleted,
        ChangeTone.Replaced,
        ChangeTone.Renamed,
        ChangeTone.Unversioned,
    ];

    /// <returns>The first of <see cref="Order"/> present; <see cref="ChangeTone.Quiet"/> when none is.</returns>
    public static ChangeTone MostUrgentOf(IEnumerable<ChangeTone> tones)
    {
        var present = tones.ToHashSet();
        return Order.FirstOrDefault(present.Contains, ChangeTone.Quiet);
    }
}
