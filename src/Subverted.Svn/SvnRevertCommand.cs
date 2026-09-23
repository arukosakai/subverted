namespace Subverted.Svn;

/// <summary>
/// Throws local changes away, via <c>svn revert</c>. The one command in Subverted that destroys
/// work nobody else has a copy of: SVN keeps no undo for it and neither do we, so whatever asks
/// for this is responsible for having confirmed it first.
/// </summary>
public sealed class SvnRevertCommand(SvnCommand command)
{
    /// <param name="paths">Absolute paths inside <paramref name="workingCopyRoot"/>.</param>
    /// <returns>SVN's notification text, one line per path it restored.</returns>
    /// <exception cref="SvnCommandException">The client failed, or refused a path.</exception>
    public async Task<string> RevertAsync(
        string workingCopyRoot,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken
    )
    {
        // Infinite depth because a caller naming a directory means the work inside it. SVN's own
        // default is this node only, which quietly does nothing on the target people actually type.
        List<string> arguments = ["revert", "--depth", "infinity", "--non-interactive"];
        arguments.AddRange(paths.Select(path => SvnTarget.Within(workingCopyRoot, path)));

        var result = await command.RunAsync(workingCopyRoot, arguments, cancellationToken);
        return result.ExitCode == 0
            ? SvnNotification.Spelled(result.StandardOutput, OperatingSystem.IsWindows())
            : throw new SvnCommandException(result.Complaint);
    }
}
