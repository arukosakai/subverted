using Subverted.Protocol;

namespace Subverted.Cli;

/// <summary>
/// What a commit that marks on the way did, as the lines to print. The marks are listed on their
/// own, not left to SVN's text alone: they are the part nobody typed a command for.
/// </summary>
public static class SelectionReport
{
    /// <summary>What was marked, then SVN's own text for the commit, which ends with the revision.</summary>
    public static IReadOnlyList<string> Committed(CommitSelectionResponse response) =>
        [
            .. Marks("marked on the way", response.Scheduled),
            .. response.Revision is null
                ? ["nothing to commit"]
                : NotificationLines.OrElse(
                    response.Notifications,
                    $"committed r{response.Revision}"
                ),
        ];

    /// <summary>
    /// Where it stopped and why, as the one line that goes to standard error.
    /// </summary>
    public static string Failure(SelectionNotCommittedResponse response) =>
        $"not committed — {WhereItStopped(response.FailedStep)}: {response.Failure.Trim()}";

    /// <summary>
    /// What the steps that finished did, and that it is all still there. A step that failed
    /// part-way may have marked paths of its own that nothing lists, which is why this points at
    /// <c>sv st</c> rather than claiming the list is complete.
    /// </summary>
    public static IReadOnlyList<string> LeftInPlace(SelectionNotCommittedResponse response) =>
        [
            .. NotificationLines.Of(response.Notifications),
            .. Marks("marked before it stopped", response.Scheduled),
            "Nothing was rolled back: these marks are still in place, and `sv st` shows exactly what "
                + "is marked now. Run the same command again once the cause is fixed, or `sv revert` "
                + "what you do not want marked.",
        ];

    private static IReadOnlyList<string> Marks(string heading, SelectionSchedule schedule)
    {
        var count = schedule.Moved.Count + schedule.Added.Count + schedule.Deleted.Count;
        if (count == 0)
        {
            return [];
        }

        return
        [
            $"{heading}: {schedule.Moved.Count} renamed, {schedule.Added.Count} added, "
                + $"{schedule.Deleted.Count} deleted",
            .. schedule.Moved.Select(move => $"  moved    {move.FromRelPath} -> {move.ToRelPath}"),
            .. schedule.Added.Select(relPath => $"  added    {relPath}"),
            .. schedule.Deleted.Select(relPath => $"  deleted  {relPath}"),
        ];
    }

    private static string WhereItStopped(SelectionStep step) =>
        step switch
        {
            SelectionStep.Move => "recording a rename failed",
            SelectionStep.Addition => "adding the new nodes failed",
            SelectionStep.Deletion => "recording the deletions failed",
            SelectionStep.Commit => "every mark was made, then sending failed",
            _ => throw new ArgumentOutOfRangeException(
                nameof(step),
                step,
                "A step this build does not know, so it cannot say where the commit stopped."
            ),
        };
}
