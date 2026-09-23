namespace Subverted.Cli;

/// <param name="Complaint">
/// What was wrong with the command line, or <c>null</c> when help was asked for outright. Decides
/// whether help goes to stdout with exit 0 or to stderr with a failure.
/// </param>
public sealed record HelpCommand(string? Complaint) : CliCommand;
