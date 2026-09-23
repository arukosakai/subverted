namespace Subverted.Cli;

/// <param name="Path">Absolute, already resolved against the working directory.</param>
public sealed record DiffCommand(string Path) : CliCommand;
