using Subverted.Protocol;

namespace Subverted.Cli;

/// <summary>
/// <c>sv commit</c> as SVN means it: versioned changes only. A missing or unversioned node is not
/// marked — that is <see cref="MarkingCommitCommand"/>, asked for with <c>--mark</c>.
/// </summary>
/// <param name="Paths">
/// Absolute, already resolved against the working directory; the current directory when none were
/// given. Only changes under these are sent, so naming a subset is how a partial commit happens.
/// </param>
/// <param name="Message">The log message, from <c>-m</c>. Never empty.</param>
public sealed record CommitCommand(IReadOnlyList<string> Paths, string Message) : CliCommand
{
    /// <summary>What goes to the daemon: a plain recursive commit, which schedules nothing on the way.</summary>
    public CommitRequest Request => new(Paths, Message);
}
