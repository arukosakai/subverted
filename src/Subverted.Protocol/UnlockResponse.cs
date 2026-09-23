namespace Subverted.Protocol;

/// <param name="Notifications">SVN's own text, one line per path it unlocked.</param>
/// <param name="Refusals">
/// The paths the server had no lock on, in SVN's own words. It still drops the local token, so this
/// is not work left undone — it is the news that the lock had already gone, which is how somebody
/// finds out theirs was stolen.
/// </param>
public sealed record UnlockResponse(string Notifications, IReadOnlyList<string> Refusals)
    : DaemonResponse;
