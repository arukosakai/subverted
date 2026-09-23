using Subverted.Protocol;

namespace Subverted.Cli;

/// <summary>
/// What a commit did, as the lines to print. SVN already ends its own output with the revision it
/// created, so nothing is added to it — the revision travels on
/// <see cref="CommitResponse.Revision"/> for front-ends that need it as a number.
/// </summary>
public static class CommitReport
{
    public static IReadOnlyList<string> Lines(CommitResponse response) =>
        response.Revision is null
            ? ["nothing to commit"]
            : NotificationLines.OrElse(response.Notifications, $"committed r{response.Revision}");
}
