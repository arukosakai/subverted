namespace Subverted.Protocol;

/// <param name="Notifications">
/// SVN's own text, one line per restored path and empty when there was nothing to restore.
/// </param>
public sealed record RevertResponse(string Notifications) : DaemonResponse;
