using Subverted.Core;

namespace Subverted.Cli;

/// <param name="Paths">
/// Absolute, already resolved against the working directory. Never empty — unlike <c>sv lock</c>
/// a directory is a perfectly good target here, but defaulting to the current one would let a bare
/// <c>sv resolve --theirs</c> overwrite every conflicted file in the tree.
/// </param>
/// <param name="AlreadyConfirmed">
/// <c>--yes</c> was given. Only consulted when <see cref="OverwritesLocalWork"/> is true.
/// </param>
public sealed record ResolveCommand(
    IReadOnlyList<string> Paths,
    ConflictResolution Resolution,
    bool AlreadyConfirmed
) : CliCommand
{
    /// <summary>
    /// Whether this resolution throws away what is in the working copy, which is what decides
    /// between asking first and simply running.
    /// </summary>
    /// <remarks>
    /// <see cref="ConflictResolution.Mine"/> rewrites the file too — it drops the merge markers —
    /// but everything it discards is the incoming revision, which is still in the repository. These
    /// two discard the local side, and for an edit nobody has committed there is no second copy.
    /// </remarks>
    public bool OverwritesLocalWork =>
        Resolution is ConflictResolution.Theirs or ConflictResolution.Base;
}
