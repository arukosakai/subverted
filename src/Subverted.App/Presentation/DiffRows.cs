using Subverted.Frontend.Diff;

namespace Subverted.App.Presentation;

/// <summary>
/// Flattens a <see cref="DiffDocument"/> into the rows a virtualised list draws, in order. Reached
/// through <see cref="DiffLayout"/>, which decides how a hunk's lines become rows.
/// </summary>
internal static class DiffRows
{
    /// <remarks>
    /// Files get a header only when there is more than one, since the pane already names a single
    /// file. A property's first hunk has no separator: its name row already opens it.
    /// </remarks>
    public static IReadOnlyList<DiffRow> Of(
        DiffDocument document,
        Func<IReadOnlyList<DiffLine>, IEnumerable<DiffRow>> rowsOfLines
    )
    {
        var rows = new List<DiffRow>();
        var isOnlyFile = document.Files.Count == 1;
        foreach (var file in document.Files)
        {
            if (!isOnlyFile)
            {
                rows.Add(new DiffFileHeaderRow(file.Path));
            }

            AddContent(rows, file, isOnlyFile, rowsOfLines);
            AddProperties(rows, file, rowsOfLines);
        }

        return rows;
    }

    private static void AddContent(
        List<DiffRow> rows,
        FileDiff file,
        bool isOnlyFile,
        Func<IReadOnlyList<DiffLine>, IEnumerable<DiffRow>> rowsOfLines
    )
    {
        switch (file.Content)
        {
            case TextChange { Hunks.Count: 0 }:
                rows.Add(new DiffNoLinesRow(file.Path));
                break;

            case TextChange text:
                foreach (var hunk in text.Hunks)
                {
                    rows.Add(new DiffHunkRow(Range(hunk, "@@")));
                    rows.AddRange(rowsOfLines(hunk.Lines));
                }
                break;

            case BinaryChange binary:
                rows.Add(new DiffBinaryRow(file.Path, binary.MimeType, isOnlyFile));
                break;
        }
    }

    private static void AddProperties(
        List<DiffRow> rows,
        FileDiff file,
        Func<IReadOnlyList<DiffLine>, IEnumerable<DiffRow>> rowsOfLines
    )
    {
        if (file.PropertyChanges.Count == 0)
        {
            return;
        }

        rows.Add(new DiffPropertySectionRow(file.Path));
        foreach (var property in file.PropertyChanges)
        {
            rows.Add(new DiffPropertyRow(property.Name, property.Kind));
            for (var index = 0; index < property.Hunks.Count; index++)
            {
                var hunk = property.Hunks[index];
                if (index > 0)
                {
                    rows.Add(new DiffHunkRow(Range(hunk, "##")));
                }

                // SVN marks nearly every property value as lacking a newline; a value is not a
                // file, so the marker would sit on almost every line and tell nobody anything.
                DiffLine[] lines =
                [
                    .. hunk.Lines.Select(line => line with { EndsWithoutNewline = false }),
                ];
                rows.AddRange(rowsOfLines(lines));
            }
        }
    }

    private static string Range(Hunk hunk, string marker) =>
        $"{marker} -{Span(hunk.OldStart, hunk.OldCount)} +{Span(hunk.NewStart, hunk.NewCount)} {marker}";

    /// <summary>A count of one is left out, as diff prints it: <c>-3</c>, not <c>-3,1</c>.</summary>
    private static string Span(int start, int count) =>
        count == 1 ? $"{start}" : $"{start},{count}";
}
