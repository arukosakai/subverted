namespace Subverted.Cli;

/// <summary>
/// Which part of a revision a log line is. Layout decides this; colour reads it — so neither has
/// to guess at the other from the text.
/// </summary>
public enum LogLine
{
    Heading,
    Path,
    Message,
}
