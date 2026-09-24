using Subverted.Core;
using Subverted.Frontend;

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
    /// <summary>Whether to ask first; see <see cref="ResolutionRisk.OverwritesLocalWork"/>.</summary>
    public bool OverwritesLocalWork => ResolutionRisk.OverwritesLocalWork(Resolution);
}
