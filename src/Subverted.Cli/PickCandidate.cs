using Subverted.Core;

namespace Subverted.Cli;

/// <summary>
/// One question the picker asks: a single changed node, or a rename made outside SVN, which is
/// two nodes that can only be sent together.
/// </summary>
/// <param name="Entry">The node, or for a rename the unversioned file at the new path.</param>
/// <param name="RenamedFrom">
/// For a rename, the missing node at the old path; <see langword="null"/> for everything else.
/// Sending one half without the other is refused, because it would end the file's history.
/// </param>
public sealed record PickCandidate(WorkingCopyEntry Entry, WorkingCopyEntry? RenamedFrom = null)
{
    public string RelPath => Entry.RelPath;

    /// <summary>
    /// Every path saying yes to this sends — both halves of a rename, the old path first.
    /// </summary>
    public IReadOnlyList<string> RelPaths =>
        RenamedFrom is null ? [Entry.RelPath] : [RenamedFrom.RelPath, Entry.RelPath];

    /// <summary>
    /// A node SVN has never heard of, which sending would add. A rename's new half is not one: it
    /// is recorded as a move, not added.
    /// </summary>
    public bool IsNew => RenamedFrom is null && Entry.Status == NodeStatus.Unversioned;
}
