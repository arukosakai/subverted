using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Cli;

/// <summary>
/// The changes a picker may offer, in the order it must offer them. Pure, because the rules in it
/// — which nodes a selection commit will take, that a rename is one question and not two, and that
/// a directory has to be answered before anything under it — are what keep a picked set committable.
/// </summary>
public static class PickCandidates
{
    /// <param name="status">A listing of the working copy the target is in, as <c>sv st</c> asks for it.</param>
    /// <param name="path">The absolute path the user named, or the working directory.</param>
    /// <param name="comparison">How this platform compares paths.</param>
    /// <returns>
    /// Committable changes under <paramref name="path"/>, ancestors first — ordinal order puts
    /// <c>src</c> before <c>src/a.txt</c> because <c>/</c> sorts below every name character. A
    /// rename sorts by its new path.
    /// </returns>
    public static IReadOnlyList<PickCandidate> Under(
        StatusResponse status,
        string path,
        StringComparison comparison
    )
    {
        var target = TargetCoverage.RelativeTo(status.Info.RootPath, path);
        bool Covered(string relPath) => TargetCoverage.Covers(target, relPath, comparison);

        var entryAt = new Dictionary<string, WorkingCopyEntry>(
            StringComparer.FromComparison(comparison)
        );
        foreach (var entry in status.Entries)
        {
            entryAt.TryAdd(entry.RelPath, entry);
        }

        List<PickCandidate> renames = [];
        foreach (var move in status.UnrecordedMoves)
        {
            // Half a rename cannot be sent, and sent as a plain delete or add it would end the
            // file's history — so a pair the path cuts in two is not offered at all.
            if (
                Covered(move.FromRelPath)
                && Covered(move.ToRelPath)
                && entryAt.TryGetValue(move.FromRelPath, out var from)
                && entryAt.TryGetValue(move.ToRelPath, out var to)
            )
            {
                renames.Add(new PickCandidate(to, RenamedFrom: from));
            }
        }

        return
        [
            .. status
                .Entries.Where(entry => !IsHalfOfARename(entry, status.UnrecordedMoves, comparison))
                .Where(IsCommittable)
                .Where(entry => Covered(entry.RelPath))
                .Select(entry => new PickCandidate(entry))
                .Concat(renames)
                .OrderBy(candidate => candidate.RelPath, StringComparer.Ordinal),
        ];
    }

    private static bool IsHalfOfARename(
        WorkingCopyEntry entry,
        IReadOnlyList<UnrecordedMove> moves,
        StringComparison comparison
    ) =>
        moves.Any(move =>
            move.FromRelPath.Equals(entry.RelPath, comparison)
            || move.ToRelPath.Equals(entry.RelPath, comparison)
        );

    /// <summary>
    /// What a selection commit accepts. An unversioned node is added and a missing one has its
    /// deletion recorded on the way. A conflicted or obstructed node is left out because SVN
    /// refuses it; <c>sv st</c> still shows every one of them.
    /// </summary>
    private static bool IsCommittable(WorkingCopyEntry entry) =>
        !entry.IsConflicted
        && entry.Status switch
        {
            NodeStatus.Modified
            or NodeStatus.Added
            or NodeStatus.Deleted
            or NodeStatus.Replaced
            or NodeStatus.Unversioned
            or NodeStatus.Missing => true,

            // The metadata fast path can prove unmodified but never modified, so an undecided node
            // is offered rather than hidden: a commit sends nothing for one that turns out clean.
            NodeStatus.NeedsPristineCompare => true,

            // SVN's second column. A property-only change leaves the content axis reading clean,
            // and a picker that trusted that axis alone would never offer it.
            NodeStatus.Unmodified => entry.PropertyStatus == PropertyStatus.Modified,

            _ => false,
        };
}
