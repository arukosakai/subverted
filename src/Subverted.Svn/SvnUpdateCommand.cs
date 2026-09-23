using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// Brings a path in line with the server, via <c>svn update</c>. A server round trip like log and
/// diff (D6), and the one people run most: it is how the rest of the studio's work arrives.
/// </summary>
public sealed class SvnUpdateCommand(SvnCommand command)
{
    /// <param name="path">An absolute path inside <paramref name="workingCopyRoot"/>.</param>
    /// <returns>
    /// What the working copy now stands at, and what needed a human. A non-zero
    /// <see cref="UpdateOutcome.Conflicts"/> is not a failure here — SVN merged what it could and
    /// left the rest on disk — but it is the thing a caller must not treat as a clean update.
    /// </returns>
    /// <exception cref="SvnCommandException">The client failed, or the server was unreachable.</exception>
    public async Task<UpdateOutcome> UpdateAsync(
        string workingCopyRoot,
        string path,
        CancellationToken cancellationToken
    )
    {
        // Postpone is stated rather than left to --non-interactive's default. A daemon picking a
        // side of somebody's conflict is the one outcome that cannot be undone from here, and a
        // default that changes under us would do exactly that without anything in this file moving.
        List<string> arguments =
        [
            "update",
            "--non-interactive",
            "--accept",
            "postpone",
            SvnTarget.Within(workingCopyRoot, path),
        ];

        var result = await command.RunAsync(workingCopyRoot, arguments, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new SvnCommandException(result.Complaint);
        }

        return new UpdateOutcome(
            SvnUpdateOutput.Revision(result.StandardOutput),
            SvnUpdateOutput.Conflicts(result.StandardOutput),
            SvnUpdateOutput.SkippedPaths(result.StandardOutput),
            SvnNotification.Spelled(result.StandardOutput, OperatingSystem.IsWindows())
        );
    }
}
