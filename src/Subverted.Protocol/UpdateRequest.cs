namespace Subverted.Protocol;

/// <param name="Path">
/// One absolute path. One per request on purpose: SVN updates several targets independently and
/// reports a revision for each, so a second path would mean the response had no single answer to
/// "what revision is this now".
/// </param>
public sealed record UpdateRequest(string Path) : DaemonRequest;
