namespace Subverted.Svn;

/// <summary>Which lines each <c>@@</c> block covers, given what the two texts share.</summary>
internal static class UnifiedHunks
{
    /// <param name="runs">What the texts share, in order, no run touching the next.</param>
    /// <param name="context">Unchanged lines around each change; <see cref="int.MaxValue"/> for all of them.</param>
    /// <returns>No hunk at all when the texts are equal.</returns>
    /// <remarks>
    /// Two changes share a block only when fewer than twice the context lines lie between them.
    /// Measured on 1.8.15 at its three: five between is one block, six is two — so blocks whose
    /// context would just touch stay apart, one line sooner than GNU diff joins them.
    /// </remarks>
    public static IReadOnlyList<UnifiedHunk> Group(
        IReadOnlyList<MatchedRun> runs,
        int oldCount,
        int newCount,
        int context
    )
    {
        var changes = Changes(runs, oldCount, newCount);
        var hunks = new List<UnifiedHunk>();
        var first = 0;
        while (first < changes.Count)
        {
            var last = first;
            while (
                last + 1 < changes.Count
                && UnchangedBetween(changes[last], changes[last + 1]) < 2L * context
            )
            {
                last++;
            }

            hunks.Add(Around(changes[first], changes[last], oldCount, newCount, context));
            first = last + 1;
        }

        return hunks;
    }

    /// <summary>The stretches between shared runs where either side has lines the other lacks.</summary>
    private static List<Change> Changes(IReadOnlyList<MatchedRun> runs, int oldCount, int newCount)
    {
        var changes = new List<Change>();
        var (oldAt, newAt) = (0, 0);
        foreach (var run in runs)
        {
            if (run.OldStart > oldAt || run.NewStart > newAt)
            {
                changes.Add(new Change(oldAt, run.OldStart, newAt, run.NewStart));
            }

            (oldAt, newAt) = (run.OldStart + run.Length, run.NewStart + run.Length);
        }

        if (oldAt < oldCount || newAt < newCount)
        {
            changes.Add(new Change(oldAt, oldCount, newAt, newCount));
        }

        return changes;
    }

    private static int UnchangedBetween(Change before, Change after) =>
        after.OldStart - before.OldEnd;

    /// <remarks>
    /// Lines before the first change and after the last are unchanged on both sides, so taking the
    /// same number off each side keeps the two starts and ends paired.
    /// </remarks>
    private static UnifiedHunk Around(
        Change first,
        Change last,
        int oldCount,
        int newCount,
        int context
    )
    {
        var leading = Math.Min(context, first.OldStart);
        var trailing = Math.Min(context, oldCount - last.OldEnd);
        var oldStart = first.OldStart - leading;
        var newStart = first.NewStart - leading;

        return new UnifiedHunk(
            oldStart,
            last.OldEnd + trailing - oldStart,
            newStart,
            last.NewEnd + trailing - newStart
        );
    }

    /// <summary>Old lines [OldStart, OldEnd) replaced by new lines [NewStart, NewEnd).</summary>
    private readonly record struct Change(int OldStart, int OldEnd, int NewStart, int NewEnd);
}
