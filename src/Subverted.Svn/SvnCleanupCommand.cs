using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// Unwedges a working copy, via <c>svn cleanup</c>. The one command here whose result cannot be
/// read from what the client printed: cleanup writes nothing on any outcome, so the working copy is
/// read on both sides of the run and the difference is the answer.
/// </summary>
public sealed class SvnCleanupCommand(SvnCommand command, ReadPendingCleanup readPendingCleanup)
{
    /// <param name="workingCopyRoot">
    /// The root, and the target. Cleanup is asked for at the root rather than at whatever path the
    /// caller named, because its two halves scope differently — measured on 1.8.15, a cleanup of a
    /// subtree drains the whole work queue but releases only the write locks reaching into that
    /// subtree, so a scoped run can exit zero and leave the copy still locked.
    /// </param>
    /// <returns>What was released and what ran. See <see cref="CleanupOutcome"/>.</returns>
    /// <exception cref="SvnCommandException">
    /// The client failed. Cleanup has no notion of a refused path: it either cleans the working copy
    /// or fails outright, so there is no per-path warning to read the way lock and resolve have.
    /// </exception>
    /// <exception cref="WcDbException">
    /// The working copy could not be read. This is what a path in no working copy fails with, not
    /// <c>E155007</c> — the reading comes first, so nothing has been run when it throws.
    /// </exception>
    public async Task<CleanupOutcome> CleanUpAsync(
        string workingCopyRoot,
        CancellationToken cancellationToken
    )
    {
        var before = readPendingCleanup(workingCopyRoot);

        var result = await command.RunAsync(
            workingCopyRoot,
            ["cleanup", SvnTarget.Within(workingCopyRoot, workingCopyRoot)],
            cancellationToken
        );

        if (result.ExitCode != 0)
        {
            throw new SvnCommandException(result.Complaint);
        }

        return CleanupEffect.Of(before, readPendingCleanup(workingCopyRoot));
    }
}
