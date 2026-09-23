namespace Subverted.Svn;

/// <summary>
/// Renames a versioned node, keeping its history, via <c>svn move</c>.
/// </summary>
/// <remarks>
/// Both failures worth knowing about report themselves as <c>E155010: Path '…' is not a directory</c>
/// — a destination that already exists, and a source that is missing from disk. SVN reads a
/// two-argument move with a non-directory destination as "move into that", which is why the message
/// talks about something the caller never asked for. Callers check both before running this, because
/// the second one half-applies: it records the add, fails to move the file, exits 1, and leaves
/// <em>both</em> paths missing.
/// </remarks>
public sealed class SvnMoveCommand(SvnCommand command)
{
    /// <param name="source">Absolute path of the versioned node to rename.</param>
    /// <param name="destination">Absolute path it takes, in the same working copy.</param>
    /// <returns>SVN's notification text: an <c>A</c> line for the destination and a <c>D</c> for the source.</returns>
    /// <exception cref="SvnCommandException">The client failed, or refused the move.</exception>
    public async Task<string> MoveAsync(
        string workingCopyRoot,
        string source,
        string destination,
        CancellationToken cancellationToken
    )
    {
        List<string> arguments =
        [
            "move",
            "--non-interactive",
            SvnTarget.Within(workingCopyRoot, source),
            SvnTarget.Within(workingCopyRoot, destination),
        ];

        var result = await command.RunAsync(workingCopyRoot, arguments, cancellationToken);
        return result.ExitCode == 0
            ? SvnNotification.Spelled(result.StandardOutput, OperatingSystem.IsWindows())
            : throw new SvnCommandException(result.Complaint);
    }
}
