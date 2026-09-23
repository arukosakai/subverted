namespace Subverted.Cli;

/// <param name="Paths">
/// Absolute, already resolved against the working directory; the current directory when none were
/// given. Only changes under these are sent, so naming a subset is how a partial commit happens.
/// </param>
/// <param name="Message">The log message, from <c>-m</c>. Never empty.</param>
public sealed record CommitCommand(IReadOnlyList<string> Paths, string Message) : CliCommand;
