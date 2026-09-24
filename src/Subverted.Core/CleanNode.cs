namespace Subverted.Core;

/// <summary>
/// A node with nothing to report: what <c>svn status</c> leaves out unless it is given <c>-v</c>.
/// One copy of the rule, so the daemon's filter and a front-end telling changes from the rest of an
/// everything-listing cannot disagree about which is which.
/// </summary>
public static class CleanNode
{
    /// <remarks>
    /// An <see cref="UnchangedNode"/> can still hold a lock token or a working-copy write lock, and
    /// each has its own <c>svn status</c> column, so neither is clean. An untouched node inside a
    /// copied directory is, as <c>svn status</c> hides it too.
    /// </remarks>
    public static bool Is(WorkingCopyEntry entry) =>
        UnchangedNode.Is(entry) && !entry.HasLockToken && !entry.IsWriteLocked;
}
