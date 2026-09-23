using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// What changed across a cleanup. Pure, and it has to be: <c>svn cleanup</c> writes nothing to
/// either stream on any outcome, so comparing the two reads is the only evidence there is that it
/// did anything at all.
/// </summary>
public static class CleanupEffect
{
    /// <param name="before">The working copy as it was when cleanup was asked for.</param>
    /// <param name="after">The same reading, taken once the client had run.</param>
    /// <returns>
    /// The locks that went away, the queued steps that ran, and anything still held. A lock present
    /// in both readings counts as remaining, never as released.
    /// </returns>
    public static CleanupOutcome Of(PendingCleanup before, PendingCleanup after)
    {
        var survived = after
            .WriteLocks.Select(held => held.RelPath)
            .ToHashSet(StringComparer.Ordinal);

        return new CleanupOutcome(
            ReleasedWriteLocks:
            [
                .. before
                    .WriteLocks.Select(held => held.RelPath)
                    .Where(relPath => !survived.Contains(relPath))
                    .Order(StringComparer.Ordinal),
            ],
            // The queue is drained, never added to, by the run that is being measured — so what is
            // left is what did not run, and the difference is what did.
            FinishedOperations: Math.Max(
                0,
                before.UnfinishedOperations - after.UnfinishedOperations
            ),
            RemainingWriteLocks:
            [
                .. after.WriteLocks.Select(held => held.RelPath).Order(StringComparer.Ordinal),
            ]
        );
    }
}
