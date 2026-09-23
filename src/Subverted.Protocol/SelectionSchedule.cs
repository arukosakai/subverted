namespace Subverted.Protocol;

/// <summary>
/// What a <see cref="CommitSelectionRequest"/> marked in the working copy before committing,
/// spelled as status entries spell paths. Edits are absent: sending them needed no marking.
/// </summary>
/// <param name="Added">
/// Nodes scheduled for addition, including everything adding an unversioned directory scheduled
/// beneath it.
/// </param>
/// <param name="Deleted">Missing nodes whose deletion was recorded.</param>
/// <param name="Moved">Renames made outside SVN, recorded as moves.</param>
public sealed record SelectionSchedule(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Deleted,
    IReadOnlyList<RecordedMove> Moved
);
