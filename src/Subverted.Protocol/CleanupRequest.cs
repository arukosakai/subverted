namespace Subverted.Protocol;

/// <param name="Path">
/// Any path inside the working copy. It names which copy to clean, not which part of it: the daemon
/// resolves it to the root and cleans there, because a cleanup scoped to a subtree can exit zero
/// having left the copy locked. See <c>SvnCleanupCommand</c>.
/// </param>
public sealed record CleanupRequest(string Path) : DaemonRequest;
