namespace Subverted.Protocol;

/// <summary>
/// Commit exactly these nodes, recording first whatever the disk says happened to them: an
/// unversioned node is added, a missing one is deleted, a rename made outside SVN is recorded as a
/// move so its history is kept, and an edit is sent as it is. The daemon decides which from its own
/// status, so the front-end sends what was ticked and never marks anything itself.
/// </summary>
/// <remarks>
/// Committed as <see cref="Core.CommitScope.ExactlyTheseNodes"/>: a directory carries nothing that
/// was not named, apart from what adding an unversioned directory scheduled beneath it. Both halves
/// of a rename have to be named, or the request is refused before anything is touched.
/// </remarks>
/// <param name="Paths">Absolute paths, all inside one working copy.</param>
/// <param name="Message">The log message. Required — the daemon has no terminal to open an editor in.</param>
public sealed record CommitSelectionRequest(IReadOnlyList<string> Paths, string Message)
    : DaemonRequest;
