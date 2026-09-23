using Subverted.Core;

namespace Subverted.Daemon;

/// <summary>
/// Decides what each ticked node needs before it can be committed: nothing for an edit, an add for
/// an unversioned node, a recorded deletion for a missing one, and a recorded move for a rename
/// made outside SVN. Pure, so every one of those rules is a test rather than a commit to try.
/// </summary>
public static class CommitSelectionPlanner
{
    /// <param name="ticked">What the person ticked, relative to the root and slash-separated.</param>
    /// <param name="entries">The whole working copy's status, unmodified nodes included.</param>
    /// <param name="unrecordedMoves">The renames D27 paired by content in that same status.</param>
    /// <param name="comparison">How this platform compares paths.</param>
    public static CommitSelectionPlan Plan(
        IReadOnlyList<string> ticked,
        IReadOnlyList<WorkingCopyEntry> entries,
        IReadOnlyList<UnrecordedMove> unrecordedMoves,
        StringComparison comparison
    )
    {
        var comparer = StringComparer.FromComparison(comparison);
        var distinct = ticked.Distinct(comparer).ToList();
        var isTicked = distinct.ToHashSet(comparer);
        var entryAt = new Dictionary<string, WorkingCopyEntry>(comparer);
        foreach (var entry in entries)
        {
            entryAt.TryAdd(entry.RelPath, entry);
        }

        List<UnrecordedMove> moves = [];
        List<WorkingCopyEntry> additions = [];
        List<string> deletions = [];
        List<string> targets = [];
        List<string> refusals = [];

        foreach (var relPath in distinct)
        {
            // Before the node's own status: both halves read as a plain `!` and `?`, and treating
            // them that way is exactly the delete-and-add that ends the file's history.
            if (PairContaining(relPath, unrecordedMoves, comparison) is { } move)
            {
                var otherHalf = move.FromRelPath.Equals(relPath, comparison)
                    ? move.ToRelPath
                    : move.FromRelPath;
                if (!isTicked.Contains(otherHalf))
                {
                    refusals.Add(HalfOfARename(move));
                    continue;
                }

                if (!moves.Contains(move))
                {
                    moves.Add(move);
                }

                targets.Add(relPath);
                continue;
            }

            if (!entryAt.TryGetValue(relPath, out var node))
            {
                refusals.Add(
                    $"'{relPath}' is neither versioned nor on disk — it may have gone since the "
                        + "list was shown."
                );
                continue;
            }

            if (RefusalFor(node) is { } refusal)
            {
                refusals.Add(refusal);
                continue;
            }

            if (node.Status == NodeStatus.Unversioned)
            {
                additions.Add(node);
            }
            else if (node.Status == NodeStatus.Missing)
            {
                deletions.Add(relPath);
            }

            targets.Add(relPath);
        }

        return new CommitSelectionPlan(moves, additions, deletions, targets, refusals);
    }

    private static UnrecordedMove? PairContaining(
        string relPath,
        IReadOnlyList<UnrecordedMove> moves,
        StringComparison comparison
    ) =>
        moves.FirstOrDefault(move =>
            move.FromRelPath.Equals(relPath, comparison)
            || move.ToRelPath.Equals(relPath, comparison)
        );

    private static string HalfOfARename(UnrecordedMove move) =>
        $"'{move.FromRelPath}' and '{move.ToRelPath}' are one rename. Tick both to record it with "
        + "its history, or neither.";

    /// <returns>Why SVN would refuse this node, or <see langword="null"/> when it can be committed.</returns>
    private static string? RefusalFor(WorkingCopyEntry entry)
    {
        if (entry.IsConflicted || entry.Status == NodeStatus.Conflicted)
        {
            return $"'{entry.RelPath}' is in conflict. Resolve it first — SVN will not commit it.";
        }

        return entry.Status switch
        {
            NodeStatus.Obstructed =>
                $"'{entry.RelPath}' is versioned as one kind and is the other on disk. Put that "
                    + "right before committing it.",
            NodeStatus.Ignored =>
                $"'{entry.RelPath}' is ignored. If it belongs in the repository, add it by hand "
                    + "first.",
            NodeStatus.External =>
                $"'{entry.RelPath}' is an svn:externals checkout, a working copy of its own. "
                    + "Commit it from there.",
            NodeStatus.Incomplete =>
                $"'{entry.RelPath}' is incomplete — an update was interrupted. Update it before "
                    + "committing.",
            _ => null,
        };
    }
}
