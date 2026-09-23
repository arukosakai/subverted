namespace Subverted.Cli;

/// <param name="Paths">
/// Absolute, already resolved against the working directory. Never empty: <c>sv rm</c> with no
/// target would mean the current directory, which is the whole project.
/// </param>
/// <param name="AlreadyConfirmed">
/// <c>--yes</c> was given. Without it the front-end lists what would go and asks first — and says
/// separately which of it SVN keeps no copy of.
/// </param>
public sealed record RemoveCommand(IReadOnlyList<string> Paths, bool AlreadyConfirmed) : CliCommand;
