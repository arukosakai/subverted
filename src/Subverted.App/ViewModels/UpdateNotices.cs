using System.Globalization;
using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>
/// What the view says about an update's answer. An update that merged around a conflict still
/// finished, but it is never drawn as a clean one: the conflict is on disk waiting for somebody.
/// </summary>
public static class UpdateNotices
{
    private const string PartWay =
        "Some of it may already have come down; the list shows where things stand.";

    private const string ConflictsHint =
        "The files are on disk with SVN's markers beside them, pinned at the top of the list.";

    private const string SkippedHint =
        "Nothing was updated where SVN skipped; its text names them.";

    public static Notice For(DaemonResponse response) =>
        response switch
        {
            UpdateResponse updated => ForFinished(updated),
            ErrorResponse error => new Notice(
                NoticeKind.Uncertain,
                "The update did not finish",
                error.Message,
                PartWay
            ),
            _ => new Notice(
                NoticeKind.Uncertain,
                "The update's answer was not understood",
                $"The daemon answered with {response.GetType().Name}, which is not an update.",
                PartWay
            ),
        };

    public static Notice Unreachable(string message) =>
        new(NoticeKind.Uncertain, "The daemon is not answering", message, PartWay);

    private static Notice ForFinished(UpdateResponse updated)
    {
        var reached = updated.Revision is { } revision
            ? string.Create(CultureInfo.InvariantCulture, $"Updated to r{revision}")
            : "Updated";
        var detail = string.IsNullOrWhiteSpace(updated.Notifications)
            ? null
            : updated.Notifications.TrimEnd();

        List<string> left = [];
        List<string> hints = [];
        if (updated.Conflicts > 0)
        {
            left.Add(Counted(updated.Conflicts, "conflict", "conflicts") + " to resolve");
            hints.Add(ConflictsHint);
        }

        if (updated.SkippedPaths > 0)
        {
            left.Add(Counted(updated.SkippedPaths, "path", "paths") + " skipped");
            hints.Add(SkippedHint);
        }

        return left.Count == 0
            ? new Notice(NoticeKind.Succeeded, reached, detail, null)
            : new Notice(
                NoticeKind.NeedsAttention,
                string.Join(" · ", [reached, .. left]),
                detail,
                string.Join(" ", hints)
            );
    }

    private static string Counted(int count, string one, string many) =>
        string.Create(CultureInfo.InvariantCulture, $"{count:N0} {(count == 1 ? one : many)}");
}
