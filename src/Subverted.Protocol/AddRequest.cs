namespace Subverted.Protocol;

/// <param name="Paths">
/// Absolute paths, all inside one working copy. The daemon resolves the first to a root and
/// rejects the request if any of the others fall outside it.
/// </param>
public sealed record AddRequest(IReadOnlyList<string> Paths) : DaemonRequest;
