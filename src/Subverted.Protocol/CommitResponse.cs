namespace Subverted.Protocol;

/// <param name="Revision">
/// What the server created, or <see langword="null"/> when there was nothing to commit. Null is
/// success, not failure — a front-end that treats it as an error will cry wolf on every no-op.
/// </param>
/// <param name="Notifications">SVN's own per-path text, unparsed.</param>
public sealed record CommitResponse(long? Revision, string Notifications) : DaemonResponse;
