using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Cli;

/// <summary>
/// The changed nodes a picker may offer, in the order it must offer them. Pure, because the two
/// rules in it — which nodes <c>svn commit</c> will take as a target, and that a directory has to
/// be answered before anything under it — are what keep a picked set committable.
/// </summary>
public static class PickCandidates
{
    /// <param name="status">A listing of the working copy the target is in, as <c>sv st</c> asks for it.</param>
    /// <param name="path">The absolute path the user named, or the working directory.</param>
    /// <param name="comparison">How this platform compares paths.</param>
    /// <returns>
    /// Committable changes under <paramref name="path"/>, ancestors first — ordinal order puts
    /// <c>src</c> before <c>src/a.txt</c> because <c>/</c> sorts below every name character.
    /// </returns>
    public static IReadOnlyList<WorkingCopyEntry> Under(
        StatusResponse status,
        string path,
        StringComparison comparison
    )
    {
        var target = TargetCoverage.RelativeTo(status.Info.RootPath, path);

        return
        [
            .. status
                .Entries.Where(IsCommittable)
                .Where(entry => TargetCoverage.Covers(target, entry.RelPath, comparison))
                .OrderBy(entry => entry.RelPath, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// What <c>svn commit</c> accepts as a target. A conflicted, missing or obstructed node is left
    /// out because naming one fails the whole commit, and an unversioned one because it has to be
    /// added first; <c>sv st</c> still shows every one of them.
    /// </summary>
    private static bool IsCommittable(WorkingCopyEntry entry) =>
        !entry.IsConflicted
        && entry.Status switch
        {
            NodeStatus.Modified or NodeStatus.Added or NodeStatus.Deleted or NodeStatus.Replaced =>
                true,

            // The metadata fast path can prove unmodified but never modified, so an undecided node
            // is offered rather than hidden: a commit sends nothing for one that turns out clean.
            NodeStatus.NeedsPristineCompare => true,

            // SVN's second column. A property-only change leaves the content axis reading clean,
            // and a picker that trusted that axis alone would never offer it.
            NodeStatus.Unmodified => entry.PropertyStatus == PropertyStatus.Modified,

            _ => false,
        };
}
