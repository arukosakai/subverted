using Subverted.Core;

namespace Subverted.Daemon;

/// <summary>
/// Which of a scan's entries a status listing should carry. Applied in the daemon rather than in
/// the front-end: a clean hundred-thousand-file checkout answers with an empty list instead of
/// serialising a hundred thousand nodes nobody asked about.
/// </summary>
public static class StatusFilter
{
    /// <param name="includeUnmodified">Keep nodes with nothing to report — <c>svn status -v</c>.</param>
    /// <param name="includeIgnored">Keep ignored nodes — <c>svn status --no-ignore</c>.</param>
    public static IReadOnlyList<WorkingCopyEntry> Apply(
        IReadOnlyList<WorkingCopyEntry> entries,
        bool includeUnmodified,
        bool includeIgnored
    )
    {
        var kept = new List<WorkingCopyEntry>();
        foreach (var entry in entries)
        {
            // Ignored is asked for by name. `-v` means "show me the clean ones too", never "show
            // me the build tree", so it does not let an ignored node through on its own.
            if (entry.Status == NodeStatus.Ignored)
            {
                if (includeIgnored)
                {
                    kept.Add(entry);
                }

                continue;
            }

            if (includeUnmodified || HasSomethingToReport(entry))
            {
                kept.Add(entry);
            }
        }

        return kept;
    }

    /// <param name="scope">
    /// Targets relative to the root, each covering itself and everything beneath it; null is the
    /// whole working copy.
    /// </param>
    /// <param name="comparison">How this platform compares paths.</param>
    public static IReadOnlyList<WorkingCopyEntry> Within(
        IReadOnlyList<WorkingCopyEntry> entries,
        IReadOnlyList<string>? scope,
        StringComparison comparison
    ) =>
        scope is null
            ? entries
            : [.. entries.Where(entry => IsInScope(entry.RelPath, scope, comparison))];

    /// <summary>
    /// The renames whose note explains a line in the scoped listing: either half in view is enough,
    /// because a file dragged into another folder leaves one half outside any single target.
    /// </summary>
    public static IReadOnlyList<UnrecordedMove> MovesWithin(
        IReadOnlyList<UnrecordedMove> moves,
        IReadOnlyList<string>? scope,
        StringComparison comparison
    ) =>
        scope is null
            ? moves
            :
            [
                .. moves.Where(move =>
                    IsInScope(move.FromRelPath, scope, comparison)
                    || IsInScope(move.ToRelPath, scope, comparison)
                ),
            ];

    private static bool IsInScope(
        string relPath,
        IReadOnlyList<string> scope,
        StringComparison comparison
    ) => scope.Any(target => TargetCoverage.Covers(target, relPath, comparison));

    /// <summary>
    /// A held lock token, a conflict and a working-copy write lock are all reported by
    /// <c>svn status</c> in their own columns even when the content and the properties are clean,
    /// so none of them is "nothing to report".
    /// </summary>
    /// <remarks>
    /// The write lock is the one that would otherwise be hidden exactly when it matters: the
    /// directory it sits on is usually unmodified, and it is the reason every write to this working
    /// copy is about to fail.
    /// </remarks>
    private static bool HasSomethingToReport(WorkingCopyEntry entry) =>
        entry.Status != NodeStatus.Unmodified
        || entry.PropertyStatus != PropertyStatus.Unmodified
        || entry.IsConflicted
        || entry.HasLockToken
        || entry.IsWriteLocked;
}
