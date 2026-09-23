using System.Globalization;

namespace Subverted.App.Presentation;

/// <summary>
/// Whether a loaded revision matches what was typed in the History search box. Only what is
/// loaded is searched: finding an older revision means scrolling to it, not a server query.
/// </summary>
public static class RevisionSearch
{
    /// <remarks>
    /// Case-insensitive against the message, the author and every changed path. A number — with
    /// or without its <c>r</c> — matches that revision exactly, never one that merely contains the
    /// digits, so <c>r12</c> does not also find r120.
    /// </remarks>
    public static bool Matches(RevisionRow row, string query)
    {
        var typed = query.Trim();
        if (typed.Length == 0)
        {
            return true;
        }

        if (RevisionNumberIn(typed) is { } revision)
        {
            return row.Revision == revision;
        }

        return Contains(row.Message, typed)
            || Contains(row.Author, typed)
            || row.ChangedPaths.Any(path => Contains(path.Path, typed));
    }

    private static long? RevisionNumberIn(string typed)
    {
        var digits = typed.StartsWith('r') || typed.StartsWith('R') ? typed[1..] : typed;
        return
            digits.Length > 0
            && long.TryParse(
                digits,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var number
            )
            ? number
            : null;
    }

    private static bool Contains(string text, string typed) =>
        text.Contains(typed, StringComparison.OrdinalIgnoreCase);
}
