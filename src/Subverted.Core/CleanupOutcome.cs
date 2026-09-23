namespace Subverted.Core;

/// <summary>
/// What a cleanup actually did. <c>svn cleanup</c> prints nothing at all — no stdout, no stderr,
/// exit zero — whether it unwedged a working copy or found nothing to do, so this is read out of
/// wc.db on either side of the run rather than parsed from its output.
/// </summary>
/// <param name="ReleasedWriteLocks">
/// The directories that were write-locked and no longer are, slash-separated with the root as the
/// empty string. Empty means the working copy was not locked, which is a real answer.
/// </param>
/// <param name="FinishedOperations">
/// How many queued steps of an interrupted operation were run. Any at all and <c>svn</c> itself had
/// been refusing to read this working copy.
/// </param>
/// <param name="RemainingWriteLocks">
/// Locks still held after the run. A cleanup that fails throws rather than reporting, so the way
/// this fills is another client taking a lock between the run and the reading that follows it —
/// rare, and the one outcome where the working copy is still wedged after a cleanup that said it
/// worked.
/// </param>
public sealed record CleanupOutcome(
    IReadOnlyList<string> ReleasedWriteLocks,
    int FinishedOperations,
    IReadOnlyList<string> RemainingWriteLocks
)
{
    /// <summary>Nothing was wrong and nothing was done.</summary>
    public bool FoundNothingToDo =>
        ReleasedWriteLocks.Count == 0 && FinishedOperations == 0 && RemainingWriteLocks.Count == 0;
}
