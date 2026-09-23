namespace Subverted.Protocol;

/// <param name="Revision">The revision the working copy now stands at.</param>
/// <param name="Conflicts">
/// Text, property and tree conflicts together, as SVN counted them. Non-zero is not a failed
/// update — what SVN could merge it merged — but a front-end that reports it as a clean one is
/// telling somebody their working copy is fine while it holds conflict markers.
/// </param>
/// <param name="SkippedPaths">How many paths SVN declined to touch.</param>
/// <param name="Notifications">SVN's own per-path text, unparsed.</param>
public sealed record UpdateResponse(
    long? Revision,
    int Conflicts,
    int SkippedPaths,
    string Notifications
) : DaemonResponse;
