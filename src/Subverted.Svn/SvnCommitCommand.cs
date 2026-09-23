using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// Sends local changes to the server, via <c>svn commit</c>. A server round trip like log and diff
/// (D6), and the one that matters most: whatever this sends is what the rest of the studio pulls.
/// </summary>
public sealed class SvnCommitCommand(SvnCommand command)
{
    /// <param name="paths">
    /// Absolute paths inside <paramref name="workingCopyRoot"/>. Only changes under these are sent,
    /// which is what makes committing a subset of a working copy possible at all.
    /// </param>
    /// <param name="scope">How far the commit reaches below each of <paramref name="paths"/>.</param>
    /// <exception cref="SvnCommandException">The client failed, or the server refused.</exception>
    public async Task<CommitOutcome> CommitAsync(
        string workingCopyRoot,
        IReadOnlyList<string> paths,
        string message,
        CommitScope scope,
        CancellationToken cancellationToken
    )
    {
        // The message goes through --message rather than an editor or a file: a daemon has no
        // terminal to open one in, and --non-interactive means a password prompt fails loudly
        // instead of leaving the commit hanging with nobody to answer it.
        List<string> arguments = ["commit", "--non-interactive", "--message", message];
        if (scope == CommitScope.ExactlyTheseNodes)
        {
            arguments.AddRange(["--depth", "empty"]);
        }

        arguments.AddRange(paths.Select(path => SvnTarget.Within(workingCopyRoot, path)));

        var result = await command.RunAsync(workingCopyRoot, arguments, cancellationToken);
        return result.ExitCode == 0
            ? new CommitOutcome(
                SvnCommitOutput.Revision(result.StandardOutput),
                SvnNotification.Spelled(result.StandardOutput, OperatingSystem.IsWindows())
            )
            : throw new SvnCommandException(result.Complaint);
    }
}
