using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Cli;

/// <summary>
/// A status response as the lines to print. Pure: the whole of what <c>sv st</c> shows is decided
/// here and written out by someone else, so the layout is testable without a terminal.
/// </summary>
public static class StatusReport
{
    /// <summary>Only what has something to report, the way <c>svn status</c> answers by default.</summary>
    public static IReadOnlyList<string> Compact(StatusResponse response, Paint paint) =>
        Render(response, StatusLine.Compact, paint);

    /// <summary>Every node the daemon carried, with its revision — <c>svn status -v</c>.</summary>
    public static IReadOnlyList<string> Verbose(StatusResponse response, Paint paint) =>
        Render(response, StatusLine.Verbose, paint);

    private static IReadOnlyList<string> Render(
        StatusResponse response,
        Func<WorkingCopyEntry, string> format,
        Paint paint
    )
    {
        var lines = new List<string>();

        // Ahead of the listing, unlike the notes below: those explain one letter in one line,
        // while this one qualifies every line there is.
        if (response.UnfinishedOperations > 0)
        {
            lines.Add(
                $"warning: {response.UnfinishedOperations} interrupted operation(s) queued here — "
                    + "`svn` itself will not read this working copy until `sv cleanup` runs, so "
                    + "what follows is Subverted's reading alone."
            );
            lines.Add(string.Empty);
        }

        foreach (var entry in ByPath(response.Entries.Where(entry => entry.Changelist is null)))
        {
            lines.Add(paint(format(entry), entry));
        }

        // Changelists are the nearest thing SVN has to a staging area, so they are worth showing
        // as groups rather than as a column nobody scans for.
        foreach (
            var changelist in response
                .Entries.Where(entry => entry.Changelist is not null)
                .GroupBy(entry => entry.Changelist!)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
        )
        {
            lines.Add(string.Empty);
            lines.Add($"--- Changelist '{changelist.Key}':");
            lines.AddRange(ByPath(changelist).Select(entry => paint(format(entry), entry)));
        }

        Note(
            NodeStatus.NeedsPristineCompare,
            $"{StatusLine.Undecided} is a node Subverted cannot decide without SVN's "
                + "translation rules; `svn status` on that path can."
        );

        Note(
            NodeStatus.Obstructed,
            $"{StatusLine.Letter(NodeStatus.Obstructed)} is a node versioned as one kind and "
                + "present on disk as the other — a file where a directory now is, or the reverse."
        );

        // After the listing, because what it explains is the pairs of `!` and `?` lines above it:
        // SVN sees those as a delete and an unrelated add, and committing them that way is what
        // ends the file's history at its old name.
        if (response.UnrecordedMoves.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add(
                $"{response.UnrecordedMoves.Count} file(s) above look renamed outside SVN — the "
                    + "content of a missing node is sitting at a path SVN does not know. "
                    + "`sv mv OLD NEW` records the rename and keeps the history:"
            );
            lines.AddRange(
                response.UnrecordedMoves.Select(move => $"  {move.FromRelPath} -> {move.ToRelPath}")
            );
        }

        return lines;

        void Note(NodeStatus status, string explanation)
        {
            if (!response.Entries.Any(entry => entry.Status == status))
            {
                return;
            }

            lines.Add(string.Empty);
            lines.Add(explanation);
        }
    }

    /// <summary>
    /// Path order, the way <c>svn status</c> prints it. The daemon hands back versioned nodes in
    /// wc.db order with unversioned ones appended, which puts an unversioned file a long way from
    /// the versioned one it sits next to on disk.
    /// </summary>
    private static IEnumerable<WorkingCopyEntry> ByPath(IEnumerable<WorkingCopyEntry> entries) =>
        entries.OrderBy(entry => entry.RelPath, StringComparer.Ordinal);
}
