using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// A whole working copy's status, via <c>svn status --xml</c>. This is D1's documented fallback:
/// the answer for a working copy whose wc.db this build cannot read, at the client's price rather
/// than the fast path's.
/// </summary>
public sealed class SvnStatusCommand(SvnCommand command)
{
    /// <param name="workingCopyRoot">
    /// The root. The client runs in it and reports every path relative to it.
    /// </param>
    /// <exception cref="SvnCommandException">The client failed, or wrote something unreadable.</exception>
    public async Task<IReadOnlyList<WorkingCopyEntry>> ReadAsync(
        string workingCopyRoot,
        CancellationToken cancellationToken
    )
    {
        // --verbose for the unmodified nodes and --no-ignore for the ignored ones. The daemon holds
        // a whole working copy and filters per request (see StatusFilter), so a scan that carried
        // only what changed could not answer `sv st -v` at all.
        List<string> arguments =
        [
            "status",
            "--xml",
            "--verbose",
            "--no-ignore",
            "--non-interactive",
            SvnTarget.Within(workingCopyRoot, workingCopyRoot),
        ];

        var result = await command.RunAsync(workingCopyRoot, arguments, cancellationToken);
        return result.ExitCode == 0
            ? SvnStatusXml.Parse(result.StandardOutput, Path.DirectorySeparatorChar)
            : throw new SvnCommandException(result.Complaint);
    }
}
