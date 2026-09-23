namespace Subverted.Protocol;

/// <summary>Which BASE revisions a path and everything below it are at — <c>svnversion</c>'s question.</summary>
/// <param name="Path">Any absolute path inside the working copy.</param>
public sealed record WorkingCopyRevisionRequest(string Path) : DaemonRequest;
