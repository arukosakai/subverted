using System.Globalization;

namespace Subverted.App.Presentation;

/// <summary>A revision as the History list draws it: the row, and where it stands against BASE.</summary>
/// <param name="Presence">Null while the BASE range is unknown, when no row is marked either way.</param>
/// <param name="BaseMarkerAbove">
/// The "your copy is here" line drawn above this row, or null for none. It sits above the newest
/// shown revision the copy has, at least in part.
/// </param>
public sealed record RevisionListItem(
    RevisionRow Row,
    RevisionPresence? Presence,
    string? BaseMarkerAbove
)
{
    public bool IsNotInCopy => Presence == RevisionPresence.NotInCopy;

    public bool IsPartlyInCopy => Presence == RevisionPresence.PartlyInCopy;

    /// <summary>
    /// What a screen reader says for the line. The BASE marker is said first, as it is drawn above
    /// the row, and the presence tags as they read on screen.
    /// </summary>
    public string AutomationName =>
        (BaseMarkerAbove is null ? string.Empty : $"{BaseMarkerAbove}. ")
        + $"r{Row.Revision.ToString(CultureInfo.InvariantCulture)}, {Row.Summary}, by {Row.Author}"
        + (Row.When.Length == 0 ? string.Empty : $", {Row.When}")
        + Presence switch
        {
            RevisionPresence.NotInCopy => ", not in your copy",
            RevisionPresence.PartlyInCopy => ", in part of your copy",
            _ => string.Empty,
        };
}
