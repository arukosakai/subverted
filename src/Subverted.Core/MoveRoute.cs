namespace Subverted.Core;

/// <summary>
/// What a rename turned out to be, once the working copy was looked at. Two of these do the move
/// and three refuse it — which of them applies is decided by what is on disk, not by what was asked
/// for, because the same command means different work depending on whether the rename already
/// happened.
/// </summary>
public enum MoveRoute
{
    /// <summary>The source is there and the destination is free: an ordinary <c>svn move</c>.</summary>
    Ordinary,

    /// <summary>
    /// The rename already happened outside SVN, and is being recorded after the fact. This is the
    /// case <c>svn move</c> cannot do on its own — see <c>UnrecordedMoveRepair</c>.
    /// </summary>
    AlreadyRenamed,

    /// <summary>
    /// Neither path holds anything. There is no rename here to record, and SVN would say so as
    /// <c>E155010: Path '…' is not a directory</c>, which explains nothing.
    /// </summary>
    NothingAtSource,

    /// <summary>
    /// Both paths hold something. SVN reads this as a move *into* the destination and reports the
    /// same misleading <c>is not a directory</c>; overwriting is never what was meant.
    /// </summary>
    DestinationOccupied,

    /// <summary>
    /// A directory was renamed outside SVN. Recording it after the fact would mean reverting the
    /// whole subtree to its pristine and then merging every local change back over the result, which
    /// is a different and much less safe operation than the one file case.
    /// </summary>
    AlreadyRenamedDirectory,
}
