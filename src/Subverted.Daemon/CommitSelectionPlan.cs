using Subverted.Core;

namespace Subverted.Daemon;

/// <summary>
/// What each ticked node means, worked out before anything is written. Every path is relative to
/// the root and slash-separated, as status entries spell them.
/// </summary>
/// <param name="Moves">Renames made outside SVN whose two halves were both ticked.</param>
/// <param name="Additions">Unversioned nodes; a directory among them brings what is beneath it.</param>
/// <param name="Deletions">Missing nodes.</param>
/// <param name="CommitTargets">
/// Every ticked node once, both halves of each move included, in the order they were ticked. What
/// adding a directory schedules beneath it is not here yet — nobody can know it until the add ran.
/// </param>
/// <param name="Refusals">
/// Why nodes cannot be committed, in words. Any at all means the request is refused whole, before
/// anything is touched.
/// </param>
public sealed record CommitSelectionPlan(
    IReadOnlyList<UnrecordedMove> Moves,
    IReadOnlyList<WorkingCopyEntry> Additions,
    IReadOnlyList<string> Deletions,
    IReadOnlyList<string> CommitTargets,
    IReadOnlyList<string> Refusals
);
