using System.Globalization;
using Subverted.Core;

namespace Subverted.App.Presentation;

/// <summary>One path a revision touched, shaped for the History view's paths list.</summary>
/// <param name="Path">Repository-absolute, as <c>svn log</c> prints it; what a revision diff is asked about.</param>
/// <param name="Name">The last segment, or <c>/</c> for the repository root.</param>
/// <param name="Folder">Everything before it, without the trailing slash; empty directly under the root.</param>
/// <param name="CopiedFrom">Where a copy came from, as <c>/path@revision</c>, or null.</param>
public sealed record ChangedPathRow(
    string Path,
    string Name,
    string Folder,
    ChangeBadge Badge,
    string? CopiedFrom
)
{
    /// <summary>What a screen reader says for the line: name, folder, badge, and a copy's source.</summary>
    public string AutomationName =>
        (Folder.Length == 0 ? $"{Name}, {Badge.Label}" : $"{Name} in {Folder}, {Badge.Label}")
        + (CopiedFrom is null ? string.Empty : $", from {CopiedFrom}");

    public static ChangedPathRow From(ChangedPath changed)
    {
        var trimmed = changed.Path.TrimEnd('/');
        var separator = trimmed.LastIndexOf('/');
        var name = trimmed.Length == 0 ? "/" : trimmed[(separator + 1)..];
        var folder = separator <= 0 ? string.Empty : trimmed[..separator];

        return new ChangedPathRow(
            changed.Path,
            name,
            folder,
            BadgeFor(changed.Change),
            CopySourceOf(changed)
        );
    }

    private static ChangeBadge BadgeFor(PathChange change) =>
        change switch
        {
            PathChange.Added => new ChangeBadge("Added", ChangeTone.Added),
            PathChange.Deleted => new ChangeBadge("Deleted", ChangeTone.Deleted),
            PathChange.Modified => new ChangeBadge("Modified", ChangeTone.Modified),
            PathChange.Replaced => new ChangeBadge("Replaced", ChangeTone.Replaced),
            _ => throw new ArgumentOutOfRangeException(nameof(change), change, null),
        };

    private static string? CopySourceOf(ChangedPath changed) =>
        (changed.CopiedFromPath, changed.CopiedFromRevision) is ({ } path, { } revision)
            ? $"{path}@{revision.ToString(CultureInfo.InvariantCulture)}"
            : null;
}
