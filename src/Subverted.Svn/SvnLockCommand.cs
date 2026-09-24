using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// Takes and gives back repository locks, via <c>svn lock</c> and <c>svn unlock</c>. A server round
/// trip like the rest of D6, and the pair a studio of binary assets lives on: nothing can merge a
/// <c>.psd</c>, so the lock is the only thing that stops two people editing one.
/// </summary>
public sealed class SvnLockCommand(SvnCommand command)
{
    /// <param name="paths">
    /// Absolute paths inside <paramref name="workingCopyRoot"/>. Files — SVN will not lock a
    /// directory, and says so rather than locking what is under it.
    /// </param>
    /// <param name="comment">
    /// What the lock is for, shown to whoever else tries the path. <see langword="null"/> for none.
    /// </param>
    /// <returns>What SVN locked, and what it refused. See <see cref="LockOutcome.Refusals"/>.</returns>
    /// <exception cref="SvnCommandException">
    /// The client failed outright — a directory, a path in no working copy, or a server it could
    /// not reach. A path somebody else holds is not this: it comes back as a refusal.
    /// </exception>
    public Task<LockOutcome> LockAsync(
        string workingCopyRoot,
        IReadOnlyList<string> paths,
        string? comment,
        ForeignLock foreign,
        CancellationToken cancellationToken
    )
    {
        List<string> arguments = ["lock", "--non-interactive"];
        if (comment is not null)
        {
            arguments.AddRange(["--message", comment]);
        }

        return RunAsync(workingCopyRoot, arguments, paths, foreign, cancellationToken);
    }

    /// <param name="paths">Absolute paths inside <paramref name="workingCopyRoot"/>.</param>
    /// <returns>What SVN unlocked, and what it refused.</returns>
    /// <exception cref="SvnCommandException">
    /// The client failed outright. A path this working copy never locked is one of these, and it
    /// releases <em>none</em> of the paths it was given — the check happens before the server is
    /// asked, so one wrong name in a list leaves every lock in it held.
    /// </exception>
    public Task<LockOutcome> UnlockAsync(
        string workingCopyRoot,
        IReadOnlyList<string> paths,
        ForeignLock foreign,
        CancellationToken cancellationToken
    ) =>
        RunAsync(
            workingCopyRoot,
            ["unlock", "--non-interactive"],
            paths,
            foreign,
            cancellationToken
        );

    private async Task<LockOutcome> RunAsync(
        string workingCopyRoot,
        List<string> arguments,
        IReadOnlyList<string> paths,
        ForeignLock foreign,
        CancellationToken cancellationToken
    )
    {
        if (foreign == ForeignLock.Overridden)
        {
            arguments.Add("--force");
        }

        arguments.AddRange(paths.Select(path => SvnTarget.Within(workingCopyRoot, path)));

        var result = await command.RunAsync(workingCopyRoot, arguments, cancellationToken);
        var onlyRefused =
            result.ExitCode == 0 || SvnRefusalSummary.IsAllThatFailed(result.StandardError);
        return onlyRefused
            ? new LockOutcome(
                SvnNotification.Spelled(result.StandardOutput, OperatingSystem.IsWindows()),
                SvnWarnings.From(result.StandardError)
            )
            : throw new SvnCommandException(result.Complaint);
    }
}
