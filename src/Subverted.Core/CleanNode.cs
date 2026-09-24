namespace Subverted.Core;

/// <summary>
/// A node with nothing to report: what <c>svn status</c> leaves out unless it is given <c>-v</c>.
/// One copy of the rule, so the daemon's filter and a front-end telling changes from the rest of an
/// everything-listing cannot disagree about which is which.
/// </summary>
public static class CleanNode
{
    /// <remarks>
    /// A held lock token, a conflict and a working-copy write lock each have their own
    /// <c>svn status</c> column and print on content- and property-clean nodes, so none is clean.
    /// An untouched node inside a copied directory is, as <c>svn status</c> hides it too.
    /// </remarks>
    public static bool Is(WorkingCopyEntry entry) =>
        entry.Status == NodeStatus.Unmodified
        && entry.PropertyStatus == PropertyStatus.Unmodified
        && !entry.IsConflicted
        && !entry.HasLockToken
        && !entry.IsWriteLocked;
}
