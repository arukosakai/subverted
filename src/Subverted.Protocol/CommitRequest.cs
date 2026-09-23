using Subverted.Core;

namespace Subverted.Protocol;

/// <param name="Paths">
/// Absolute paths, all inside one working copy. Only changes under these are sent, so naming a
/// subset is how a partial commit happens.
/// </param>
/// <param name="Message">The log message. Required — the daemon has no terminal to open an editor in.</param>
/// <param name="Scope">
/// Whether each path carries what is below it. A front-end that asked about each node one at a time
/// sends <see cref="CommitScope.ExactlyTheseNodes"/>, or a picked directory would take the children
/// its owner just declined.
/// </param>
public sealed record CommitRequest(
    IReadOnlyList<string> Paths,
    string Message,
    CommitScope Scope = CommitScope.WholeSubtree
) : DaemonRequest;
