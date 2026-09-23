using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// Which directories a working copy's write locks reach. One <c>WC_LOCK</c> row lights up more than
/// one directory, so the rows cannot be matched to entries by path alone.
/// </summary>
/// <remarks>
/// The depth rule is measured rather than documented: on 1.8.15 a lock on <c>sub</c> with
/// <c>locked_levels = 1</c> made <c>svn status</c> print <c>L</c> on <c>sub</c> and <c>sub/deep</c>
/// and not on <c>sub/deep/deeper</c>. So the count is levels *below* the locked directory.
/// </remarks>
public sealed class WriteLockCoverage(IReadOnlyList<WorkingCopyWriteLock> locks)
{
    private const int EveryLevel = -1;

    /// <summary>A working copy with nothing locked, which is every healthy one.</summary>
    public static readonly WriteLockCoverage None = new([]);

    public bool Any => locks.Count > 0;

    /// <summary>
    /// Whether <c>svn status</c> would print <c>L</c> against this node.
    /// </summary>
    /// <param name="kind">
    /// Taken rather than assumed because only directories are ever locked — wc.db keeps
    /// <c>local_dir_relpath</c> — and a file's path sits under a locked directory exactly the way a
    /// locked subdirectory's does. Deciding on the path alone reports every file in a wedged working
    /// copy as locked.
    /// </param>
    /// <param name="relPath">
    /// Slash-separated, root as the empty string — the spelling wc.db stores and entries carry.
    /// </param>
    public bool Reaches(NodeKind kind, string relPath) =>
        kind == NodeKind.Directory && locks.Any(held => Reaches(held, relPath));

    private static bool Reaches(WorkingCopyWriteLock held, string directoryRelPath) =>
        LevelsBelow(held.RelPath, directoryRelPath) is { } levels
        && (held.LockedLevels == EveryLevel || levels <= held.LockedLevels);

    /// <returns>
    /// How many path segments <paramref name="candidate"/> sits below <paramref name="ancestor"/>,
    /// or <see langword="null"/> when it does not sit below it at all. Zero means they are the same
    /// directory.
    /// </returns>
    private static int? LevelsBelow(string ancestor, string candidate)
    {
        if (string.Equals(ancestor, candidate, StringComparison.Ordinal))
        {
            return 0;
        }

        // The root is every other directory's ancestor and is the empty string, so it prefixes
        // without a separator of its own; every other ancestor needs one to rule out `subtree`
        // matching a lock on `sub`.
        var prefix = ancestor.Length == 0 ? string.Empty : ancestor + '/';
        return candidate.StartsWith(prefix, StringComparison.Ordinal)
            ? candidate.AsSpan(prefix.Length).Count('/') + 1
            : null;
    }
}
