namespace Subverted.Frontend.Diff;

/// <summary>
/// Reads a hunk's body: exactly as many lines as its header announced, so a content line that looks
/// like a header — <c>--- x</c>, <c>@@ -1 +1 @@</c>, <c>Index: foo</c> — is still content.
/// </summary>
internal static class HunkReader
{
    /// <param name="cursor">Positioned on the first line after the header; left after the last line read.</param>
    /// <returns>
    /// The hunk. It holds fewer lines than announced only when the text ends first or a line has no
    /// diff prefix — never the case in what <c>svn diff</c> prints.
    /// </returns>
    public static Hunk Read(HunkRange range, LineCursor cursor)
    {
        var lines = new List<DiffLine>();
        var oldNumber = range.OldStart;
        var newNumber = range.NewStart;
        var oldEnd = range.OldStart + range.OldCount;
        var newEnd = range.NewStart + range.NewCount;

        while (
            (oldNumber < oldEnd || newNumber < newEnd)
            && cursor.Current is { Length: > 0 } line
            && KindOf(line[0]) is { } kind
        )
        {
            cursor.Advance();
            var text = line[1..];
            var endsWithoutNewline = ConsumeNoNewlineMarker(cursor);

            switch (kind)
            {
                case DiffLineKind.Context:
                    lines.Add(
                        new DiffLine(kind, text, oldNumber++, newNumber++, endsWithoutNewline)
                    );
                    break;
                case DiffLineKind.Removed:
                    lines.Add(new DiffLine(kind, text, oldNumber++, null, endsWithoutNewline));
                    break;
                default:
                    lines.Add(new DiffLine(kind, text, null, newNumber++, endsWithoutNewline));
                    break;
            }
        }

        return new Hunk(range.OldStart, range.OldCount, range.NewStart, range.NewCount, lines);
    }

    private static DiffLineKind? KindOf(char prefix) =>
        prefix switch
        {
            ' ' => DiffLineKind.Context,
            '-' => DiffLineKind.Removed,
            '+' => DiffLineKind.Added,
            _ => null,
        };

    /// <remarks>
    /// Matched on the backslash alone: SVN prints <c>\ No newline at end of file</c> after content and
    /// <c>\ No newline at end of property</c> after a property value, and no diff line starts with one.
    /// </remarks>
    private static bool ConsumeNoNewlineMarker(LineCursor cursor)
    {
        if (cursor.Current is not { } next || !next.StartsWith('\\'))
        {
            return false;
        }

        cursor.Advance();
        return true;
    }
}
