namespace Subverted.Protocol;

/// <summary>The selection was recorded and committed.</summary>
/// <param name="Revision">
/// What the server created, or <see langword="null"/> when there turned out to be nothing to send —
/// success, as on <see cref="CommitResponse"/>.
/// </param>
/// <param name="Scheduled">What was marked before the commit, all of it now in the revision.</param>
/// <param name="Notifications">SVN's own text for every step, unparsed.</param>
public sealed record CommitSelectionResponse(
    long? Revision,
    SelectionSchedule Scheduled,
    string Notifications
) : DaemonResponse;
