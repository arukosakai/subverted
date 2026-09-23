namespace Subverted.Cli;

/// <param name="Path">
/// Absolute, already resolved against the working directory; the current directory when none was
/// given. Everything changed under it is offered one node at a time.
/// </param>
/// <param name="Message">The log message, from <c>-m</c>. Never empty.</param>
public sealed record PickCommand(string Path, string Message) : CliCommand;
