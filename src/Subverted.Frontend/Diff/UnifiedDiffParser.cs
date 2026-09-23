namespace Subverted.Frontend.Diff;

/// <summary>Turns <c>svn diff</c>'s text into a <see cref="DiffDocument"/>.</summary>
public static class UnifiedDiffParser
{
    /// <summary>
    /// Reads the output of <c>svn diff</c> run with <c>LC_ALL=C</c>, as the daemon runs it: one
    /// <see cref="FileDiff"/> per <c>Index:</c> section, in the order SVN printed them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Never throws. Text it does not recognise is skipped, not reported: anything before the first
    /// <c>Index:</c>, lines inside a section that are none of the shapes below, and a hunk whose
    /// header does not parse — whose body lines are then unrecognised too. A hunk cut short by the
    /// end of the text, or by a line with no diff prefix, keeps the lines read so far.
    /// </para>
    /// <para>
    /// Within a section: <c>@@</c> hunks give a <see cref="TextChange"/>, SVN's binary notice a
    /// <see cref="BinaryChange"/>, and a section with only <c>Property changes on:</c> leaves
    /// <see cref="FileDiff.Content"/> null. A section with none of them — an added empty file — is
    /// a <see cref="TextChange"/> with no hunks. Consecutive sections for one path are one file.
    /// </para>
    /// </remarks>
    /// <returns><see cref="DiffDocument.Empty"/> when SVN printed no section at all.</returns>
    public static DiffDocument Parse(string unifiedDiff)
    {
        var cursor = new LineCursor(DiffLines.Split(unifiedDiff));
        var files = new List<FileDiff>();

        while (cursor.Current is { } line)
        {
            if (line.StartsWith(FileSectionReader.Header, StringComparison.Ordinal))
            {
                files.Add(FileSectionReader.Read(line, cursor));
            }
            else
            {
                cursor.Advance();
            }
        }

        return files.Count == 0 ? DiffDocument.Empty : new DiffDocument(files);
    }
}
