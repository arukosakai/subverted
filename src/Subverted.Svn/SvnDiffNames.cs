namespace Subverted.Svn;

/// <summary>
/// Gives a diff back its true file names where svn's text could not hold them (D34), by asking the
/// same question again as a UTF-8 summary. Costs a second svn run, and only where a name can be lost.
/// </summary>
internal static class SvnDiffNames
{
    /// <param name="diffArguments">The arguments that produced <paramref name="diff"/>.</param>
    /// <exception cref="SvnCommandException">The summary failed where the diff had not.</exception>
    public static async Task<string> RespelledAsync(
        SvnCommand command,
        string workingDirectory,
        IReadOnlyList<string> diffArguments,
        string diff,
        CancellationToken cancellationToken
    )
    {
        if (diff.Length == 0 || !command.Spelling.CanLoseNames)
        {
            return diff;
        }

        var summary = await command.RunAsync(
            workingDirectory,
            [.. diffArguments, "--summarize", "--xml"],
            cancellationToken
        );

        return summary.ExitCode == 0
            ? DiffHeaderRespelling.Respell(
                diff,
                SvnDiffSummary.Paths(summary.StandardOutput),
                command.Spelling.AsSvnWouldPrint
            )
            : throw new SvnCommandException(summary.Complaint);
    }
}
