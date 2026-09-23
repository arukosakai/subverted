namespace Subverted.Cli;

/// <summary>
/// <c>sv commit --mark</c>: everything changed under the paths, with what the disk says happened
/// recorded on the way — a missing node deleted, an unversioned one added, a rename made outside
/// SVN kept as a move. Opt-in, because it adds files a plain <c>svn commit</c> would leave alone.
/// </summary>
/// <param name="Paths">
/// Absolute, already resolved against the working directory; the current directory when none were
/// given.
/// </param>
/// <param name="Message">The log message, from <c>-m</c>. Never empty.</param>
public sealed record MarkingCommitCommand(IReadOnlyList<string> Paths, string Message) : CliCommand;
