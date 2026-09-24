using System.Globalization;
using System.Text;

namespace Subverted.Svn;

/// <summary>
/// One file's section of a unified diff, written byte for byte as <c>svn diff</c> writes it, but
/// with as much context as asked for — the one thing the client cannot do (GUI.md, Measured).
/// </summary>
/// <remarks>
/// Measured on 1.8.15, and matched by the equivalence tests: SVN's own lines end in the platform's
/// newline while content lines keep the file's own ending; a count of one is left out of an
/// <c>@@</c> header and a side with no lines starts at 0; and a last line with no ending is given
/// the platform's newline, then <c>\ No newline at end of file</c>.
/// </remarks>
internal static class SvnStyleDiff
{
    /// <summary>The steps <see cref="LongestCommonLines"/> may take before the diff is given up on.</summary>
    internal const long SearchBudget = 10_000_000;

    private const string Rule = "===================================================================";
    private const string NoNewline = @"\ No newline at end of file";

    /// <param name="oldText">The old side, in the form SVN compares: its normal form.</param>
    /// <param name="newText">The new side, in the same form.</param>
    /// <param name="context">Unchanged lines around each change; <see langword="null"/> for the whole file.</param>
    /// <param name="newline">What ends SVN's own lines: the platform's newline, as the client writes it.</param>
    /// <param name="budget">See <see cref="LongestCommonLines.Find"/>.</param>
    /// <returns>
    /// The section; empty when the texts are equal, since SVN prints nothing for such a file; and
    /// <see langword="null"/> when finding the change would take more than the budget.
    /// </returns>
    public static byte[]? Write(
        DiffSectionHeader header,
        byte[] oldText,
        byte[] newText,
        int? context,
        string newline,
        long budget = SearchBudget
    )
    {
        var oldLines = TextLines.Split(oldText);
        var newLines = TextLines.Split(newText);
        var (oldTokens, newTokens) = LineTokens.Of(oldText, oldLines, newText, newLines);
        var runs = LongestCommonLines.Find(oldTokens, newTokens, budget);
        if (runs is null)
        {
            return null;
        }

        var hunks = UnifiedHunks.Group(
            runs,
            oldLines.Length,
            newLines.Length,
            context ?? int.MaxValue
        );
        if (hunks.Count == 0)
        {
            return [];
        }

        using var output = new MemoryStream();
        var writer = new SectionWriter(output, newline);
        writer.Header(header);
        foreach (var hunk in hunks)
        {
            writer.Hunk(hunk, runs, oldText, oldLines, newText, newLines);
        }

        return output.ToArray();
    }

    private sealed class SectionWriter(Stream output, string newline)
    {
        public void Header(DiffSectionHeader header)
        {
            Text($"Index: {header.Name}");
            Text(Rule);
            Text($"--- {header.Name}\t({header.OldLabel})");
            Text($"+++ {header.Name}\t({header.NewLabel})");
        }

        public void Hunk(
            UnifiedHunk hunk,
            IReadOnlyList<MatchedRun> runs,
            byte[] oldText,
            TextLine[] oldLines,
            byte[] newText,
            TextLine[] newLines
        )
        {
            Text($"@@ -{Range(hunk.OldStart, hunk.OldCount)} +{Range(hunk.NewStart, hunk.NewCount)} @@");

            var (oldAt, newAt) = (hunk.OldStart, hunk.NewStart);
            var (oldEnd, newEnd) = (hunk.OldStart + hunk.OldCount, hunk.NewStart + hunk.NewCount);
            var next = FirstRunEndingAfter(runs, oldAt);
            while (oldAt < oldEnd || newAt < newEnd)
            {
                var run = next < runs.Count ? runs[next] : (MatchedRun?)null;
                if (run is { } shared && shared.OldStart <= oldAt && shared.NewStart <= newAt)
                {
                    Line(' ', oldText, oldLines[oldAt]);
                    (oldAt, newAt) = (oldAt + 1, newAt + 1);
                    if (oldAt == shared.OldStart + shared.Length)
                    {
                        next++;
                    }

                    continue;
                }

                var changeOldEnd = Math.Min(run?.OldStart ?? oldEnd, oldEnd);
                var changeNewEnd = Math.Min(run?.NewStart ?? newEnd, newEnd);
                for (; oldAt < changeOldEnd; oldAt++)
                {
                    Line('-', oldText, oldLines[oldAt]);
                }

                for (; newAt < changeNewEnd; newAt++)
                {
                    Line('+', newText, newLines[newAt]);
                }
            }
        }

        /// <remarks>A side with no lines names the line before it, which at the top of a file is 0.</remarks>
        private static string Range(int start, int count)
        {
            var first = (count == 0 ? start : start + 1).ToString(CultureInfo.InvariantCulture);
            return count == 1 ? first : $"{first},{count.ToString(CultureInfo.InvariantCulture)}";
        }

        /// <summary>The first run not wholly before old line <paramref name="oldLine"/>; runs end in order.</summary>
        private static int FirstRunEndingAfter(IReadOnlyList<MatchedRun> runs, int oldLine)
        {
            var (low, high) = (0, runs.Count);
            while (low < high)
            {
                var middle = (low + high) / 2;
                if (runs[middle].OldStart + runs[middle].Length > oldLine)
                {
                    high = middle;
                }
                else
                {
                    low = middle + 1;
                }
            }

            return low;
        }

        private void Line(char sign, byte[] text, TextLine line)
        {
            output.WriteByte((byte)sign);
            output.Write(text, line.Start, line.Length);
            if (!line.HasEnding)
            {
                Text(string.Empty);
                Text(NoNewline);
            }
        }

        private void Text(string line)
        {
            output.Write(Encoding.UTF8.GetBytes(line));
            output.Write(Encoding.UTF8.GetBytes(newline));
        }
    }
}
