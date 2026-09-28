namespace Subverted.Svn;

/// <summary>
/// What an update of a path would bring down, via <c>svn status --show-updates --xml</c>. A server
/// round trip, and a local walk as well: SVN works out this copy's own status in the same run,
/// which is the price of an answer that is right for a copy updated in parts.
/// </summary>
/// <remarks>
/// Externals are left out: each is a checkout of its own, perhaps of another server, and a slow or
/// unreachable one would hold up the answer for this copy.
/// </remarks>
public sealed class SvnIncomingCommand(SvnCommand command)
{
    /// <param name="path">Absolute, inside the working copy; everything beneath it is asked about.</param>
    /// <exception cref="SvnCommandException">
    /// The client failed — the server could not be reached, or would not answer without a password —
    /// or wrote something unreadable.
    /// </exception>
    public async Task<Core.IncomingChanges> ReadAsync(
        string workingCopyRoot,
        string path,
        CancellationToken cancellationToken
    )
    {
        List<string> arguments =
        [
            "status",
            "--xml",
            "--show-updates",
            "--ignore-externals",
            "--non-interactive",
            SvnTarget.Within(workingCopyRoot, path),
        ];

        var result = await command.RunAsync(workingCopyRoot, arguments, cancellationToken);
        return result.ExitCode == 0
            ? SvnIncomingXml.Parse(result.StandardOutput, Path.DirectorySeparatorChar)
            : throw new SvnCommandException(result.Complaint);
    }
}
