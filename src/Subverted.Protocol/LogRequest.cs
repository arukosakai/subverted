namespace Subverted.Protocol;

/// <param name="Path">Any absolute path inside the working copy; history is read for that path.</param>
/// <param name="Limit">
/// How many revisions, newest first. Null asks for the whole history, which on a studio repository
/// is a long server round trip — the front-end defaults to a cap for that reason.
/// </param>
public sealed record LogRequest(string Path, int? Limit) : DaemonRequest;
