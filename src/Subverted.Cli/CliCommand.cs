namespace Subverted.Cli;

/// <summary>
/// What the user asked for. Designed for inheritance only from inside this assembly: the private
/// protected constructor keeps the set closed so dispatch stays exhaustive.
/// </summary>
public abstract record CliCommand
{
    private protected CliCommand() { }
}
