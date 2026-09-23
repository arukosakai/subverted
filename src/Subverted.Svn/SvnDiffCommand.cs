namespace Subverted.Svn;

/// <summary>
/// A path's local changes as a unified diff, via <c>svn diff</c>. The client's own text is passed
/// through verbatim — reproducing SVN's diff format, including how it renders property changes and
/// binary files, is a project of its own that would only produce a second answer to disagree with.
/// </summary>
public sealed class SvnDiffCommand(SvnCommand command)
{
    /// <returns>The diff, empty when nothing under the path has changed.</returns>
    /// <exception cref="SvnCommandException">The client failed.</exception>
    public async Task<string> ReadAsync(
        string workingCopyRoot,
        string path,
        CancellationToken cancellationToken
    )
    {
        var result = await command.RunAsync(
            workingCopyRoot,
            ["diff", "--non-interactive", SvnTarget.WithinForDiff(workingCopyRoot, path)],
            cancellationToken
        );

        return result.ExitCode == 0
            ? result.StandardOutput
            : throw new SvnCommandException(result.Complaint);
    }
}
