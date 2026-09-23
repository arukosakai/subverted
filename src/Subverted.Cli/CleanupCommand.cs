namespace Subverted.Cli;

/// <param name="Path">
/// Absolute, already resolved against the working directory. It names the working copy, not the
/// part of it to clean — the daemon cleans at the root either way. The current directory is a safe
/// default here, unlike on <c>sv revert</c>, because the target chooses nothing.
/// </param>
/// <param name="AlreadyConfirmed">
/// <c>--yes</c> was given. Cleanup asks first for the hazard SVN's own help names: it cannot tell a
/// lock left by a dead client from one a running client is relying on.
/// </param>
public sealed record CleanupCommand(string Path, bool AlreadyConfirmed) : CliCommand;
