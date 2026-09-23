namespace Subverted.Svn;

/// <summary>
/// Schedules paths for addition, via <c>svn add</c>. Nothing leaves the machine — the addition is
/// recorded in wc.db and travels on the next commit — but it does write to the working copy, so it
/// goes through the client rather than through our own reader.
/// </summary>
public sealed class SvnAddCommand(SvnCommand command)
{
    /// <param name="paths">Absolute paths inside <paramref name="workingCopyRoot"/>.</param>
    /// <returns>SVN's notification text, one line per path it scheduled.</returns>
    /// <exception cref="SvnCommandException">The client failed, or refused a path.</exception>
    public async Task<string> AddAsync(
        string workingCopyRoot,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken
    )
    {
        // --parents so adding `art/hero/final.png` under an unversioned `art/hero` works the way
        // anyone would expect, rather than failing on the directory and leaving nothing scheduled.
        List<string> arguments = ["add", "--parents", "--non-interactive"];
        arguments.AddRange(paths.Select(path => SvnTarget.Within(workingCopyRoot, path)));

        var result = await command.RunAsync(workingCopyRoot, arguments, cancellationToken);
        return result.ExitCode == 0
            ? SvnNotification.Spelled(result.StandardOutput, OperatingSystem.IsWindows())
            : throw new SvnCommandException(result.Complaint);
    }
}
