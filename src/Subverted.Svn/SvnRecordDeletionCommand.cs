namespace Subverted.Svn;

/// <summary>
/// Records the deletion of nodes already gone from disk, via <c>svn delete</c> without
/// <c>--force</c>. The caller believes they are missing; this is what keeps a stale belief safe.
/// </summary>
/// <remarks>
/// Measured on 1.8.15: a missing file or directory is scheduled and exits zero, and a file that has
/// come back with edits is refused with <c>E195006</c> and left on disk as it was. With
/// <c>--force</c>, as <see cref="SvnDeleteCommand"/> passes it, that second file would be unlinked.
/// </remarks>
public sealed class SvnRecordDeletionCommand(SvnCommand command)
{
    /// <param name="paths">Absolute paths inside <paramref name="workingCopyRoot"/>.</param>
    /// <returns>SVN's notification text, one <c>D</c> line per node it scheduled.</returns>
    /// <exception cref="SvnCommandException">The client failed, or refused a path.</exception>
    public async Task<string> RecordAsync(
        string workingCopyRoot,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken
    )
    {
        List<string> arguments = ["delete", "--non-interactive"];
        arguments.AddRange(paths.Select(path => SvnTarget.Within(workingCopyRoot, path)));

        var result = await command.RunAsync(workingCopyRoot, arguments, cancellationToken);
        return result.ExitCode == 0
            ? SvnNotification.Spelled(result.StandardOutput, OperatingSystem.IsWindows())
            : throw new SvnCommandException(result.Complaint);
    }
}
