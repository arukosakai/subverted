using Subverted.Frontend.Diff;

namespace Subverted.App.Presentation;

/// <summary>Lists a hunk's lines one per row, as <c>svn diff</c> prints them.</summary>
public static class UnifiedLines
{
    /// <returns>
    /// Every line, in its own order. Within each run of changed lines between context, the n-th
    /// removed line and the n-th added line name each other as counterparts: the same pairs
    /// <see cref="SplitLines"/> puts side by side. A line with nothing to pair has none.
    /// </returns>
    public static IReadOnlyList<DiffTextRow> Of(IReadOnlyList<DiffLine> lines)
    {
        var rows = new List<DiffTextRow>(lines.Count);
        var run = new List<DiffLine>();
        foreach (var line in lines)
        {
            if (line.Kind is DiffLineKind.Removed or DiffLineKind.Added)
            {
                run.Add(line);
                continue;
            }

            AddChangeRun(rows, run);
            rows.Add(new DiffTextRow(line));
        }

        AddChangeRun(rows, run);
        return rows;
    }

    private static void AddChangeRun(List<DiffTextRow> rows, List<DiffLine> run)
    {
        var removed = run.Where(line => line.Kind == DiffLineKind.Removed).ToList();
        var added = run.Where(line => line.Kind == DiffLineKind.Added).ToList();
        var removedSeen = 0;
        var addedSeen = 0;
        foreach (var line in run)
        {
            var counterpart =
                line.Kind == DiffLineKind.Removed
                    ? added.ElementAtOrDefault(removedSeen++)
                    : removed.ElementAtOrDefault(addedSeen++);
            rows.Add(new DiffTextRow(line, counterpart));
        }

        run.Clear();
    }
}
