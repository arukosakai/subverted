using System.Globalization;

namespace Subverted.Svn;

/// <summary>
/// What one committed revision did to one repository path, via <c>svn diff -c N URL@N</c>. Passed
/// through verbatim for the same reason as <see cref="SvnDiffCommand"/>.
/// </summary>
/// <remarks>
/// A URL rather than a working-copy path, measured on 1.8.15: <c>svn diff -c N</c> on a path that
/// is no longer on disk fails with <c>E155010</c>, and a revision's paths are routinely deleted or
/// renamed since. A copy with no edits diffs to nothing, because SVN compares it with its source.
/// </remarks>
public sealed class SvnRevisionDiffCommand(SvnCommand command)
{
    /// <param name="repositoryRoot">The working copy's repository root URL, as wc.db records it.</param>
    /// <param name="repositoryPath">Repository-absolute, as <c>svn log</c> prints a changed path.</param>
    /// <returns>The diff, with headers relative to the path; empty when nothing under it changed.</returns>
    /// <exception cref="SvnCommandException">The client failed.</exception>
    public async Task<string> ReadAsync(
        string workingCopyRoot,
        string repositoryRoot,
        string repositoryPath,
        long revision,
        CancellationToken cancellationToken
    )
    {
        var result = await command.RunAsync(
            workingCopyRoot,
            [
                "diff",
                "--non-interactive",
                "--change",
                revision.ToString(CultureInfo.InvariantCulture),
                SvnTarget.InRepository(repositoryRoot, repositoryPath, revision),
            ],
            cancellationToken
        );

        return result.ExitCode == 0
            ? result.StandardOutput
            : throw new SvnCommandException(result.Complaint);
    }
}
