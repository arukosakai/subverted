using Subverted.Core;

namespace Subverted.Cli;

/// <summary>
/// One node as one line, laid out the way <c>svn status</c> lays it out so the two can be read —
/// and diffed — side by side.
/// </summary>
public static class StatusLine
{
    /// <summary>
    /// The letter <c>svn status</c> puts in its first column, with one addition: <c>*</c> for a
    /// node Subverted cannot decide without reimplementing SVN's eol and keyword translation.
    /// </summary>
    /// <remarks>
    /// This used to be <c>~</c>, which SVN spends on "obstructed" — fine only while Subverted did
    /// not model that. It does now, so <c>~</c> went back to meaning what SVN means by it and the
    /// invented state took a character SVN never writes in column one. <c>*</c> is SVN's
    /// out-of-date marker in column *eight*, under <c>-u</c>, which is a different column.
    /// </remarks>
    public const char Undecided = '*';

    public static string Compact(WorkingCopyEntry entry) =>
        $"{Columns(entry)} {Display(entry.RelPath)}";

    /// <summary>Adds the BASE revision, the way <c>svn status -v</c> does.</summary>
    public static string Verbose(WorkingCopyEntry entry) =>
        $"{Columns(entry)} {Revision(entry), 8}  {Display(entry.RelPath)}";

    /// <summary>
    /// Seven columns, of which Subverted fills five. Columns 5 and 7 — switched and tree conflict —
    /// are not modelled, and are left blank rather than guessed at.
    /// </summary>
    private static string Columns(WorkingCopyEntry entry) =>
        new([
            Letter(entry.Status),
            entry.PropertyStatus == PropertyStatus.Modified ? 'M' : ' ',
            entry.IsWriteLocked ? 'L' : ' ',
            entry.IsCopied ? '+' : ' ',
            ' ',
            entry.HasLockToken ? 'K' : ' ',
            ' ',
        ]);

    public static char Letter(NodeStatus status) =>
        status switch
        {
            NodeStatus.Unmodified => ' ',
            NodeStatus.Modified => 'M',
            NodeStatus.Added => 'A',
            NodeStatus.Deleted => 'D',
            NodeStatus.Replaced => 'R',
            NodeStatus.Missing => '!',
            NodeStatus.Incomplete => '!',
            NodeStatus.Unversioned => '?',
            NodeStatus.Ignored => 'I',
            NodeStatus.Conflicted => 'C',
            NodeStatus.Obstructed => '~',
            NodeStatus.External => 'X',
            _ => Undecided,
        };

    /// <summary>The working-copy root has an empty relative path; <c>svn status</c> calls it <c>.</c></summary>
    private static string Display(string relPath) => relPath.Length == 0 ? "." : relPath;

    private static string Revision(WorkingCopyEntry entry) => entry.Revision?.ToString() ?? "-";
}
