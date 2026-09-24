using Subverted.Core;

namespace Subverted.App.Presentation;

/// <summary>
/// Which lines the menu offers Lock and Unlock on. Measured on 1.8.15: SVN locks a file that exists
/// in the repository at that path — edited, unchanged, missing, scheduled for deletion, replaced,
/// obstructed or conflicted — and fails with E155010 for anything added, copied or unversioned.
/// </summary>
public static class LockOffer
{
    /// <summary>A file the repository has at this path, whose lock this working copy does not hold yet.</summary>
    public static bool CanLock(ChangeRow row) =>
        row is { IsLocked: false, IsCopied: false, Entry.Kind: NodeKind.File }
        && row.Entry.Status
            is not (
                NodeStatus.Unversioned
                or NodeStatus.Ignored
                or NodeStatus.Added
                or NodeStatus.External
            );

    /// <summary>Only a lock this working copy holds can be given back from here.</summary>
    public static bool CanUnlock(ChangeRow row) => row.IsLocked;
}
