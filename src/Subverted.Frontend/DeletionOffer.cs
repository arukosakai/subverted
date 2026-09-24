using Subverted.Core;

namespace Subverted.Frontend;

/// <summary>
/// Whether a node may be deleted from a front-end, and if not, why — decided from what
/// <c>svn delete --force</c> did to each kind of node on 1.8.15 rather than from what it accepts.
/// </summary>
public static class DeletionOffer
{
    /// <param name="target">The node the person picked.</param>
    /// <param name="listing">A listing reaching at least everything beneath the target.</param>
    /// <param name="comparison">How this platform compares paths.</param>
    /// <returns>Why it is not offered, as a sentence naming the path; <c>null</c> when it is.</returns>
    public static string? RefusalFor(
        WorkingCopyEntry target,
        IEnumerable<WorkingCopyEntry> listing,
        StringComparison comparison
    )
    {
        var path = target.RelPath;
        if (path.Length == 0)
        {
            return "SVN cannot delete the root of a working copy.";
        }

        if (target.Status is NodeStatus.Unversioned or NodeStatus.Ignored)
        {
            return $"{path} is not in SVN, so there is nothing for SVN to delete. A file manager can remove it.";
        }

        if (target.Status == NodeStatus.External)
        {
            return $"{path} is an external checkout. It goes by changing svn:externals on the folder that holds it.";
        }

        if (target.IsConflicted || target.Status == NodeStatus.Conflicted)
        {
            return $"{path} is in conflict. Resolve it first: deleting it would also remove the "
                + "conflict's own files beside it, which this list cannot name.";
        }

        // Measured: the work queue fails removing a folder as a file, and cleanup retries it and fails.
        if (target.Status == NodeStatus.Obstructed)
        {
            return $"Something else is in {path}'s place on disk. Deleting it makes SVN stop part-way and "
                + "leaves a cleanup that fails too; move what is there out of the way first.";
        }

        if (target.Status == NodeStatus.Deleted)
        {
            return $"{path} is already marked deleted.";
        }

        if (target.Status == NodeStatus.Incomplete)
        {
            return $"{path} was left part-way through an update. Update it before deleting it.";
        }

        var incomplete = listing.FirstOrDefault(entry =>
            entry.Status == NodeStatus.Incomplete
            && TargetCoverage.Covers(path, entry.RelPath, comparison)
        );
        return incomplete is null
            ? null
            : $"{incomplete.RelPath} was left part-way through an update. Update it before deleting {path}.";
    }
}
