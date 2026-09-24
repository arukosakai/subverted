namespace Subverted.Svn;

/// <summary>
/// A path's local changes as a unified diff, via <c>svn diff</c>. The client's own text is passed
/// through verbatim; only a wider context than its three lines is written in-process, and only for
/// a plain text edit (<see cref="WorkingCopyContextDiff"/>, D36).
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
        string[] arguments =
        [
            "diff",
            "--non-interactive",
            SvnTarget.WithinForDiff(workingCopyRoot, path),
        ];
        var result = await command.RunAsync(workingCopyRoot, arguments, cancellationToken);

        return result.ExitCode == 0
            ? await SvnDiffNames.RespelledAsync(
                command,
                workingCopyRoot,
                arguments,
                result.StandardOutput,
                cancellationToken
            )
            : throw new SvnCommandException(result.Complaint);
    }
}
