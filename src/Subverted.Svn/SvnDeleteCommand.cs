namespace Subverted.Svn;

/// <summary>
/// Schedules nodes for deletion and removes them from disk, via <c>svn delete</c>.
/// </summary>
/// <remarks>
/// Always <c>--force</c>, because without it SVN refuses exactly the two cases a caller has already
/// had confirmed: a file with local modifications (<c>E195006</c>) and one that is not versioned at
/// all (<c>E200005</c>). Whatever asks for this is responsible for having said which it was — an
/// unversioned file has no pristine behind it, so <c>svn delete --force</c> unlinks it with nothing
/// to restore it from, silently and exiting zero.
/// </remarks>
public sealed class SvnDeleteCommand(SvnCommand command)
{
    /// <param name="paths">Absolute paths inside <paramref name="workingCopyRoot"/>.</param>
    /// <returns>
    /// SVN's notification text, one <c>D</c> line per versioned node. Empty for an unversioned path,
    /// which SVN removes without saying so.
    /// </returns>
    /// <exception cref="SvnCommandException">The client failed, or refused a path.</exception>
    public async Task<string> DeleteAsync(
        string workingCopyRoot,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken
    )
    {
        List<string> arguments = ["delete", "--force", "--non-interactive"];
        arguments.AddRange(paths.Select(path => SvnTarget.Within(workingCopyRoot, path)));

        var result = await command.RunAsync(workingCopyRoot, arguments, cancellationToken);
        return result.ExitCode == 0
            ? SvnNotification.Spelled(result.StandardOutput, OperatingSystem.IsWindows())
            : throw new SvnCommandException(result.Complaint);
    }
}
