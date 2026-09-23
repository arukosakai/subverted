namespace Subverted.Cli;

/// <param name="Paths">
/// Absolute, already resolved against the working directory, and never empty: with no path given
/// it is the working directory, which is what <c>svn status</c> lists then too.
/// </param>
/// <param name="IncludeUnmodified">The <c>-v</c> switch.</param>
/// <param name="IncludeIgnored">The <c>--no-ignore</c> switch.</param>
public sealed record StatusCommand(
    IReadOnlyList<string> Paths,
    bool IncludeUnmodified,
    bool IncludeIgnored
) : CliCommand;
