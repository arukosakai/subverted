using Subverted.Core;

namespace Subverted.Cli;

/// <param name="Paths">
/// Absolute, already resolved against the working directory. Never empty: the only default
/// available is the current directory, and a directory is the one thing SVN will not lock.
/// </param>
/// <param name="Comment">What the lock is for, or null when none was given.</param>
public sealed record LockCommand(IReadOnlyList<string> Paths, string? Comment, ForeignLock Foreign)
    : CliCommand;
