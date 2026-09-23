namespace Subverted.Cli;

/// <param name="Paths">
/// Absolute, already resolved against the working directory. Never empty: <c>sv revert</c> with no
/// target would mean the current directory, and the current directory is where someone's day is.
/// </param>
/// <param name="AlreadyConfirmed">
/// <c>--yes</c> was given. Without it the front-end lists what would be lost and asks first, which
/// is the only thing standing between a typo and an afternoon's work.
/// </param>
public sealed record RevertCommand(IReadOnlyList<string> Paths, bool AlreadyConfirmed) : CliCommand;
