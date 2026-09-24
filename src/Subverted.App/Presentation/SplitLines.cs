using Subverted.Frontend.Diff;

namespace Subverted.App.Presentation;

/// <summary>Pairs a hunk's lines into side-by-side rows.</summary>
public static class SplitLines
{
    /// <returns>
    /// A context line as one row with the line on both sides. Each run of changed lines between
    /// context becomes rows pairing its removed lines, in order, with its added lines, in order;
    /// the shorter side of the run is padded with <c>null</c>.
    /// </returns>
    public static IReadOnlyList<DiffSplitRow> Of(IReadOnlyList<DiffLine> lines)
    {
        var rows = new List<DiffSplitRow>();
        var removed = new List<DiffLine>();
        var added = new List<DiffLine>();
        foreach (var line in lines)
        {
            switch (line.Kind)
            {
                case DiffLineKind.Removed:
                    removed.Add(line);
                    break;

                case DiffLineKind.Added:
                    added.Add(line);
                    break;

                default:
                    AddChangeRun(rows, removed, added);
                    rows.Add(new DiffSplitRow(line, line));
                    break;
            }
        }

        AddChangeRun(rows, removed, added);
        return rows;
    }

    private static void AddChangeRun(
        List<DiffSplitRow> rows,
        List<DiffLine> removed,
        List<DiffLine> added
    )
    {
        var height = Math.Max(removed.Count, added.Count);
        for (var index = 0; index < height; index++)
        {
            rows.Add(
                new DiffSplitRow(removed.ElementAtOrDefault(index), added.ElementAtOrDefault(index))
            );
        }

        removed.Clear();
        added.Clear();
    }
}
