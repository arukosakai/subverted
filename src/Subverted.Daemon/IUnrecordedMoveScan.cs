using Subverted.Core;

namespace Subverted.Daemon;

/// <summary>
/// Pairs missing nodes with the unversioned files holding their content, which is how a rename made
/// outside SVN is recognised. Separate from <see cref="IWorkingCopyScan"/> because only the wc.db
/// reader can answer it: the pairing is against the checksum SVN recorded, and the CLI fallback
/// never sees one.
/// </summary>
public interface IUnrecordedMoveScan
{
    /// <param name="entries">The listing just produced, so this costs no second walk of the tree.</param>
    /// <returns>The pairs, ordered by the path that went missing; empty when nothing is missing.</returns>
    /// <remarks>
    /// Synchronous, and called only while the session holds its scanning lock: this shares one
    /// SQLite connection with the scan itself.
    /// </remarks>
    IReadOnlyList<UnrecordedMove> FindUnrecordedMoves(IReadOnlyList<WorkingCopyEntry> entries);
}
