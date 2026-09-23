namespace Subverted.Cli;

/// <summary>
/// What one line of a unified diff is. Classification reads the prefix; colour reads this — so
/// neither has to know about the other's table.
/// </summary>
public enum DiffLine
{
    FileHeader,
    HunkHeader,
    Added,
    Removed,
    Context,
}
