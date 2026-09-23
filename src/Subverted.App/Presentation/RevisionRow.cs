using System.Globalization;
using Subverted.Core;

namespace Subverted.App.Presentation;

/// <summary>One revision in the History list, already shaped for display.</summary>
/// <param name="Author">Never empty: a revision committed without authentication reads as such.</param>
/// <param name="When">The commit time in the viewer's zone, or empty when SVN recorded none.</param>
/// <param name="Summary">The message's first non-blank line, which is what a list row has room for.</param>
/// <param name="Message">The whole message, trimmed, for the details card.</param>
public sealed record RevisionRow(
    long Revision,
    string Author,
    string When,
    string Summary,
    string Message,
    IReadOnlyList<ChangedPathRow> ChangedPaths
)
{
    public const string NoAuthor = "(no author)";

    public const string NoMessage = "(no message)";

    /// <param name="zone">The viewer's zone: SVN records UTC, and an artist reads their own clock.</param>
    public static RevisionRow From(RevisionEntry entry, TimeZoneInfo zone)
    {
        var message = entry.Message.Trim();
        var summary = message
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0);

        return new RevisionRow(
            entry.Revision,
            string.IsNullOrWhiteSpace(entry.Author) ? NoAuthor : entry.Author,
            entry.Date is { } date
                ? TimeZoneInfo
                    .ConvertTime(date, zone)
                    .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                : string.Empty,
            summary ?? NoMessage,
            message,
            [.. entry.ChangedPaths.Select(ChangedPathRow.From)]
        );
    }
}
