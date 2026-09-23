using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Cli;

/// <summary>
/// A revision history as the lines to print. Closer to <c>git log</c> than to <c>svn log</c>'s
/// rows of dashes, which cost a line per revision and carry nothing.
/// </summary>
public static class LogReport
{
    /// <param name="limit">
    /// What the request asked for. A listing exactly that long is probably not the whole history,
    /// and saying so is the difference between "that is all of it" and "that is all you asked
    /// for". Null when nothing was capped.
    /// </param>
    /// <param name="zone">
    /// Taken rather than read, so the dates a test asserts on do not depend on where the build
    /// machine happens to be.
    /// </param>
    public static IReadOnlyList<string> Lines(
        LogResponse response,
        int? limit,
        TimeZoneInfo zone,
        PaintLog paint
    )
    {
        if (response.Revisions.Count == 0)
        {
            return ["no revisions"];
        }

        var lines = new List<string>();
        foreach (var revision in response.Revisions)
        {
            if (lines.Count > 0)
            {
                lines.Add(string.Empty);
            }

            lines.Add(paint(Heading(revision, zone), LogLine.Heading));
            lines.AddRange(
                revision.ChangedPaths.Select(changed =>
                    paint($"    {Letter(changed.Change)} {Describe(changed)}", LogLine.Path)
                )
            );

            // An empty message is what SVN stores for a commit made without one. Nothing to print,
            // and nothing has gone wrong.
            lines.AddRange(
                MessageLines(revision.Message).Select(line => paint($"    {line}", LogLine.Message))
            );
        }

        if (limit is { } asked && response.Revisions.Count == asked)
        {
            lines.Add(string.Empty);
            lines.Add(paint($"(newest {asked}; --all for the rest)", LogLine.Heading));
        }

        return lines;
    }

    private static IEnumerable<string> MessageLines(string message) =>
        message.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0);

    private static string Heading(RevisionEntry revision, TimeZoneInfo zone) =>
        $"r{revision.Revision}  {revision.Author ?? "(no author)"}  {Moment(revision.Date, zone)}";

    private static string Moment(DateTimeOffset? date, TimeZoneInfo zone) =>
        date is { } committed
            ? TimeZoneInfo.ConvertTime(committed, zone).ToString("yyyy-MM-dd HH:mm")
            : "(no date)";

    private static string Describe(ChangedPath changed) =>
        changed.CopiedFromPath is { } source
            ? $"{changed.Path} (from {source}@{changed.CopiedFromRevision})"
            : changed.Path;

    /// <remarks>
    /// The arms are in declaration order and the last member is the discard: the compiler insists
    /// on one, and this way it is an arm a test reaches rather than a branch nothing can cover.
    /// </remarks>
    private static char Letter(PathChange change) =>
        change switch
        {
            PathChange.Added => 'A',
            PathChange.Deleted => 'D',
            PathChange.Modified => 'M',
            _ => 'R',
        };
}
