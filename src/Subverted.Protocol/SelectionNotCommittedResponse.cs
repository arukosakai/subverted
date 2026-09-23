namespace Subverted.Protocol;

/// <summary>
/// Nothing reached the server, and the working copy may have been changed on the way: a step that
/// writes ran, and then something failed. The schedule is left in place — no rollback — so the next
/// status reads <c>A</c> and <c>D</c> where it stopped, and sending the same request again works.
/// </summary>
/// <remarks>
/// Distinct from <see cref="ErrorResponse"/>, which from this request always means nothing was
/// written. A client step that fails part-way may have scheduled some of its own paths, which
/// <paramref name="Scheduled"/> does not list; the next status does.
/// </remarks>
/// <param name="Scheduled">What the steps that finished marked.</param>
/// <param name="FailedStep">
/// Where it stopped. <see cref="SelectionStep.Commit"/> means every mark was made and only sending
/// failed.
/// </param>
/// <param name="Notifications">SVN's own text for the steps that finished.</param>
/// <param name="Failure">Why it stopped: SVN's own error text, or why a rename could not be recorded.</param>
public sealed record SelectionNotCommittedResponse(
    SelectionSchedule Scheduled,
    SelectionStep FailedStep,
    string Notifications,
    string Failure
) : DaemonResponse;
