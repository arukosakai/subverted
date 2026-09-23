using Subverted.Core;

namespace Subverted.Cli;

/// <param name="Paths">
/// Absolute, already resolved against the working directory. Never empty, for the reason given on
/// <see cref="LockCommand"/> — and because one unnamed path in a list releases none of them.
/// </param>
public sealed record UnlockCommand(IReadOnlyList<string> Paths, ForeignLock Foreign) : CliCommand;
