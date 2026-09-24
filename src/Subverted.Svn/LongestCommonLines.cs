namespace Subverted.Svn;

/// <summary>
/// Which lines two texts share, as the fewest insertions and deletions that turn one into the
/// other — found the way <c>svn diff</c> finds them, so that where several smallest answers exist
/// this picks the one SVN picks.
/// </summary>
/// <remarks>
/// Follows Subversion 1.8.15's <c>libsvn_diff/lcs.c</c> and the prefix and suffix scans of
/// <c>diff_file.c</c>: shared leading lines are taken first, all but <see cref="SuffixLinesKept"/>
/// shared trailing lines are taken off, lines only one side has are passed over, and the O(NP)
/// search of Wu, Manber and Myers visits diagonals in SVN's order and breaks ties as it does.
/// </remarks>
internal static class LongestCommonLines
{
    /// <summary>Shared trailing lines left in the search, as SVN leaves them (<c>SUFFIX_LINES_TO_KEEP</c>).</summary>
    internal const int SuffixLinesKept = 50;

    /// <param name="oldLines">One token per line; equal tokens are equal lines.</param>
    /// <param name="newLines">The same, for the other side.</param>
    /// <param name="budget">
    /// Steps the search may take. Its cost grows with the length of the texts times the lines
    /// changed, so this is what bounds a rewrite of a huge file.
    /// </param>
    /// <returns>
    /// The shared runs in order, no run touching the next, or <see langword="null"/> when the
    /// search ran out of budget first.
    /// </returns>
    public static IReadOnlyList<MatchedRun>? Find(
        ReadOnlySpan<int> oldLines,
        ReadOnlySpan<int> newLines,
        long budget
    )
    {
        var prefix = oldLines.CommonPrefixLength(newLines);
        var sharedSuffix = SharedSuffix(oldLines[prefix..], newLines[prefix..]);
        var suffix = Math.Max(0, sharedSuffix - SuffixLinesKept);

        var middle = SearchPastUniqueLines(
            oldLines[prefix..^suffix],
            newLines[prefix..^suffix],
            budget
        );
        if (middle is null)
        {
            return null;
        }

        var runs = new List<MatchedRun>(middle.Count + 2);
        if (prefix > 0)
        {
            runs.Add(new MatchedRun(0, 0, prefix));
        }

        foreach (var run in middle)
        {
            Append(runs, new MatchedRun(run.OldStart + prefix, run.NewStart + prefix, run.Length));
        }

        if (suffix > 0)
        {
            Append(
                runs,
                new MatchedRun(oldLines.Length - suffix, newLines.Length - suffix, suffix)
            );
        }

        return runs;
    }

    private static int SharedSuffix(ReadOnlySpan<int> a, ReadOnlySpan<int> b)
    {
        var shared = 0;
        while (shared < a.Length && shared < b.Length && a[^(shared + 1)] == b[^(shared + 1)])
        {
            shared++;
        }

        return shared;
    }

    /// <summary>Joins a run onto the one before it when the two touch.</summary>
    private static void Append(List<MatchedRun> runs, MatchedRun run)
    {
        if (
            runs.Count > 0
            && runs[^1] is var last
            && last.OldStart + last.Length == run.OldStart
            && last.NewStart + last.Length == run.NewStart
        )
        {
            runs[^1] = last with { Length = last.Length + run.Length };
            return;
        }

        runs.Add(run);
    }

    /// <summary>
    /// SVN's search steps over a line only one side has without counting it. Taking such lines
    /// out first and mapping the runs back is the same search, and splits a run wherever one fell.
    /// </summary>
    private static List<MatchedRun>? SearchPastUniqueLines(
        ReadOnlySpan<int> oldLines,
        ReadOnlySpan<int> newLines,
        long budget
    )
    {
        var oldKept = LinesAlsoIn(oldLines, newLines, out var oldIndex);
        var newKept = LinesAlsoIn(newLines, oldLines, out var newIndex);
        if (Search(oldKept, newKept, budget) is not { } found)
        {
            return null;
        }

        var runs = new List<MatchedRun>(found.Count);
        foreach (var run in found)
        {
            for (var i = 0; i < run.Length; i++)
            {
                Append(
                    runs,
                    new MatchedRun(oldIndex[run.OldStart + i], newIndex[run.NewStart + i], 1)
                );
            }
        }

        return runs;
    }

    private static int[] LinesAlsoIn(
        ReadOnlySpan<int> lines,
        ReadOnlySpan<int> other,
        out int[] originalIndex
    )
    {
        var present = new HashSet<int>(other.Length);
        foreach (var token in other)
        {
            present.Add(token);
        }

        var kept = new List<int>(lines.Length);
        var index = new List<int>(lines.Length);
        for (var i = 0; i < lines.Length; i++)
        {
            if (present.Contains(lines[i]))
            {
                kept.Add(lines[i]);
                index.Add(i);
            }
        }

        originalIndex = [.. index];
        return [.. kept];
    }

    /// <summary>
    /// <c>svn_diff__lcs</c>. Diagonal k is the old side's remaining length less the new side's, so
    /// the search starts on M − N and ends on 0; each diagonal holds the furthest new line reached
    /// for the current number of costly moves p, and -1 before it is reached.
    /// </summary>
    private static List<MatchedRun>? Search(int[] a, int[] b, long budget)
    {
        var d = a.Length - b.Length;
        var offset = b.Length + 1;
        var reached = new int[a.Length + b.Length + 3];
        var chains = new Chain?[reached.Length];
        Array.Fill(reached, -1);

        var steps = 0L;
        for (var p = 0; ; p++)
        {
            for (var k = Math.Min(d, 0) - p; k < 0; k++)
            {
                steps += Snake(a, b, d, k, offset, reached, chains);
            }

            for (var k = Math.Max(d, 0) + p; k >= 0; k--)
            {
                steps += Snake(a, b, d, k, offset, reached, chains);
            }

            if (reached[offset] == b.Length)
            {
                return Chain.InOrder(chains[offset]);
            }

            if (steps > budget)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// <c>svn_diff__snake</c>: steps onto diagonal <paramref name="k"/> from the neighbour that
    /// reached further — taking a new line when the two tie, as SVN does — then along every line
    /// the sides share. The diagonal before the start holds -1, so the first step lands on line 0.
    /// </summary>
    /// <returns>The steps taken, for the budget.</returns>
    private static int Snake(
        int[] a,
        int[] b,
        int d,
        int k,
        int offset,
        int[] reached,
        Chain?[] chains
    )
    {
        var at = k + offset;
        var fromInsertion = reached[at - 1] >= reached[at + 1];
        var y = fromInsertion ? reached[at - 1] + 1 : reached[at + 1];
        var chain = fromInsertion ? chains[at - 1] : chains[at + 1];
        var x = d + y - k;

        var (startX, startY) = (x, y);
        while (x < a.Length && y < b.Length && a[x] == b[y])
        {
            x++;
            y++;
        }

        reached[at] = y;
        chains[at] =
            x > startX ? new Chain(new MatchedRun(startX, startY, x - startX), chain) : chain;
        return 1 + x - startX;
    }

    /// <summary>The runs one path found, newest first, sharing its start with the paths it branched from.</summary>
    private sealed class Chain(MatchedRun run, Chain? previous)
    {
        private readonly MatchedRun _run = run;
        private readonly Chain? _previous = previous;

        public static List<MatchedRun> InOrder(Chain? last)
        {
            var runs = new List<MatchedRun>();
            for (var link = last; link is not null; link = link._previous)
            {
                runs.Add(link._run);
            }

            runs.Reverse();
            return runs;
        }
    }
}
