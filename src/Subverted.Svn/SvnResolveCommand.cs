using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// Marks conflicted nodes resolved, via <c>svn resolve</c>. The command that finishes what
/// <c>sv up</c> starts: an update that conflicts leaves the working copy needing a person, and
/// until this there was nothing in Subverted that person could use.
/// </summary>
public sealed class SvnResolveCommand(SvnCommand command)
{
    /// <param name="paths">
    /// Absolute paths inside <paramref name="workingCopyRoot"/>. Directories are fine and are the
    /// normal case — this always recurses, for the reason on <see cref="Arguments"/>.
    /// </param>
    /// <returns>
    /// What SVN resolved and what it refused. An empty <see cref="ResolveOutcome.ResolvedPaths"/>
    /// with no refusals means nothing under these paths was conflicted, which is a real answer and
    /// not a failure.
    /// </returns>
    /// <exception cref="SvnCommandException">
    /// The client failed outright. A refused path is one of these <em>and</em> a warning: unlike
    /// <c>svn lock</c>, resolve reports a refusal in its exit code as well — but it is still
    /// per-path, so the other targets were resolved and the warnings say which were not.
    /// </exception>
    public async Task<ResolveOutcome> ResolveAsync(
        string workingCopyRoot,
        IReadOnlyList<string> paths,
        ConflictResolution resolution,
        CancellationToken cancellationToken
    )
    {
        var result = await command.RunAsync(
            workingCopyRoot,
            Arguments(workingCopyRoot, paths, resolution),
            cancellationToken
        );

        var outcome = new ResolveOutcome(
            SvnResolveOutput.ResolvedPaths(result.StandardOutput, OperatingSystem.IsWindows()),
            SvnWarnings.From(result.StandardError)
        );

        // A refused path exits non-zero and is still a refusal, so the warnings have to win over the
        // exit code here; anything else non-zero is the client itself failing and must be thrown.
        return result.ExitCode == 0 || outcome.Refusals.Count > 0
            ? outcome
            : throw new SvnCommandException(result.Complaint);
    }

    /// <remarks>
    /// Two flags are not optional and both are load-bearing. <c>--accept</c> is always passed
    /// because <c>svn resolve</c> without it <b>waits on stdin forever</b>, even with stdin at
    /// end-of-file, and a daemon that did that would hang holding the request; <c>--recursive</c>
    /// is always passed because resolve's default depth is <c>empty</c>, so naming a directory
    /// resolves nothing and says nothing while exiting zero.
    /// </remarks>
    private static List<string> Arguments(
        string workingCopyRoot,
        IReadOnlyList<string> paths,
        ConflictResolution resolution
    ) =>
        [
            "resolve",
            "--non-interactive",
            "--recursive",
            "--accept",
            Accept(resolution),
            .. paths.Select(path => SvnTarget.Within(workingCopyRoot, path)),
        ];

    private static string Accept(ConflictResolution resolution) =>
        resolution switch
        {
            ConflictResolution.Working => "working",
            ConflictResolution.Mine => "mine-full",
            ConflictResolution.Theirs => "theirs-full",
            ConflictResolution.Base => "base",
            _ => throw new ArgumentOutOfRangeException(nameof(resolution)),
        };
}
