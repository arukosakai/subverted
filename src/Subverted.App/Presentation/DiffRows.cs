using Subverted.Frontend.Diff;

namespace Subverted.App.Presentation;

/// <summary>Flattens a <see cref="DiffDocument"/> into the rows a virtualised list draws, in order.</summary>
public static class DiffRows
{
    /// <remarks>
    /// Files get a header only when there is more than one, since the pane already names a single
    /// file. A property's first hunk has no separator: its name row already opens it.
    /// </remarks>
    public static IReadOnlyList<DiffRow> Of(DiffDocument document)
    {
        var rows = new List<DiffRow>();
        var isOnlyFile = document.Files.Count == 1;
        foreach (var file in document.Files)
        {
            if (!isOnlyFile)
            {
                rows.Add(new DiffFileHeaderRow(file.Path));
            }

            AddContent(rows, file, isOnlyFile);
            AddProperties(rows, file);
        }

        return rows;
    }

    private static void AddContent(List<DiffRow> rows, FileDiff file, bool isOnlyFile)
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
                    AddLines(rows, hunk);
                }
                break;

            case BinaryChange binary:
                rows.Add(new DiffBinaryRow(file.Path, binary.MimeType, isOnlyFile));
                break;
        }
    }

    private static void AddProperties(List<DiffRow> rows, FileDiff file)
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
                foreach (var line in hunk.Lines)
                {
                    rows.Add(new DiffTextRow(line with { EndsWithoutNewline = false }));
                }
            }
        }
    }

    private static void AddLines(List<DiffRow> rows, Hunk hunk)
    {
        foreach (var line in hunk.Lines)
        {
            rows.Add(new DiffTextRow(line));
        }
    }

    private static string Range(Hunk hunk, string marker) =>
        $"{marker} -{Span(hunk.OldStart, hunk.OldCount)} +{Span(hunk.NewStart, hunk.NewCount)} {marker}";

    /// <summary>A count of one is left out, as diff prints it: <c>-3</c>, not <c>-3,1</c>.</summary>
    private static string Span(int start, int count) =>
        count == 1 ? $"{start}" : $"{start},{count}";
}
