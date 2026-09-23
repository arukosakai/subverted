namespace Subverted.Cli;

/// <param name="Path">Absolute, already resolved against the working directory.</param>
/// <param name="Limit">
/// How many revisions, newest first. Null is <c>--all</c>, and is the only way to ask for a whole
/// studio history — see <see cref="CommandLine.DefaultRevisionLimit"/> for why it is not the
/// default.
/// </param>
public sealed record LogCommand(string Path, int? Limit) : CliCommand;
