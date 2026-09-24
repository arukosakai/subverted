namespace Subverted.Core;

/// <summary>
/// A node holding nothing to commit or revert: its content and properties match BASE and nothing is
/// in conflict. Wider than <see cref="CleanNode"/>, which also needs no lock mark to be printed.
/// </summary>
public static class UnchangedNode
{
    /// <remarks>
    /// A held lock token and a working-copy write lock are states of the copy, not edits in it: a
    /// commit has nothing to send for either, so neither makes a node changed.
    /// </remarks>
    public static bool Is(WorkingCopyEntry entry) =>
        entry.Status == NodeStatus.Unmodified
        && entry.PropertyStatus == PropertyStatus.Unmodified
        && !entry.IsConflicted;
}
