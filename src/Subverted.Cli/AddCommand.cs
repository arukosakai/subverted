namespace Subverted.Cli;

/// <param name="Paths">
/// Absolute, already resolved against the working directory. Never empty — there is no useful
/// reading of <c>sv add</c> with no target, and guessing one would schedule a build tree.
/// </param>
public sealed record AddCommand(IReadOnlyList<string> Paths) : CliCommand;
