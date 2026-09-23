namespace Subverted.Cli;

/// <param name="Source">Absolute path of the node to rename, or of where it used to be.</param>
/// <param name="Destination">Absolute path it takes.</param>
public sealed record MoveCommand(string Source, string Destination) : CliCommand;
