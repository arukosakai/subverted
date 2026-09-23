namespace Subverted.Core;

/// <param name="RelPath">Slash-separated path relative to the working-copy root.</param>
/// <param name="Status">
/// Content and tree state only. A node whose properties alone changed is <see
/// cref="NodeStatus.Unmodified"/> here and <see cref="PropertyStatus.Modified"/> on
/// <paramref name="PropertyStatus"/>, so asking this axis alone will call it clean.
/// </param>
/// <param name="PropertyStatus">Versioned-property state — SVN's second status column.</param>
/// <param name="Revision">BASE revision, or null for nodes that exist only locally.</param>
/// <param name="Changelist">SVN changelist name — the closest thing SVN has to a staging area.</param>
/// <param name="HasLockToken">
/// This working copy holds a repository lock on the node — <c>svn status</c>'s sixth column,
/// <c>K</c>. Files only, and a different thing entirely from <paramref name="IsWriteLocked"/>.
/// </param>
/// <param name="IsWriteLocked">
/// A client is part-way through changing this directory, or died while it was — <c>svn status</c>'s
/// third column, <c>L</c>. Directories only. Everything that writes to the working copy fails with
/// <c>E155004</c> while it stands, and <c>sv cleanup</c> is what releases it.
/// </param>
/// <param name="IsCopied">
/// Scheduled with history — <c>svn status</c>'s fourth column, <c>+</c>. True for a copy or move
/// and everything it carried, and false when the node is missing or obstructed, because what is on
/// disk is then not the copy. Not something to report on its own: an untouched copied file is clean.
/// </param>
public sealed record WorkingCopyEntry(
    string RelPath,
    NodeKind Kind,
    NodeStatus Status,
    PropertyStatus PropertyStatus,
    long? Revision,
    string? Changelist,
    bool IsConflicted,
    bool HasLockToken,
    bool IsWriteLocked,
    bool IsCopied
);
