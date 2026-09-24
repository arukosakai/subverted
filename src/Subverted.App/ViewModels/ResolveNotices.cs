using System.Globalization;
using Subverted.App.Presentation;
using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>
/// What the view says about a resolve's answer. <c>svn resolve</c> exits zero having done nothing
/// for a path with no conflict, so the count is what tells "resolved" from "nothing to resolve".
/// </summary>
public static class ResolveNotices
{
    private const string PartWay =
        "Some of it may already be resolved; the list shows what is left.";

    /// <param name="target">The resolved path as the list shows it.</param>
    public static Notice For(string target, ConflictResolution kept, DaemonResponse response) =>
        response switch
        {
            ResolveResponse { Refusals.Count: > 0 } refused => Refused(target, kept, refused),
            ResolveResponse { ResolvedPaths.Count: 0 } => new Notice(
                NoticeKind.Succeeded,
                $"Nothing in {target} was conflicted",
                null,
                null
            ),
            ResolveResponse resolved => Resolved(target, kept, resolved.ResolvedPaths),
            ErrorResponse error => new Notice(
                NoticeKind.Uncertain,
                $"{target} was not resolved",
                error.Message,
                PartWay
            ),
            _ => new Notice(
                NoticeKind.Uncertain,
                "The resolve's answer was not understood",
                $"The daemon answered with {response.GetType().Name}, which is not a resolve.",
                PartWay
            ),
        };

    /// <summary>Confirmed after the listing stopped showing a conflict under the target.</summary>
    public static Notice NothingLeft(string target) =>
        new(NoticeKind.NothingWritten, $"Nothing left to resolve in {target}", null, null);

    public static Notice Unreachable(string message) =>
        new(NoticeKind.Uncertain, "The daemon is not answering", message, PartWay);

    private static Notice Resolved(
        string target,
        ConflictResolution kept,
        IReadOnlyList<string> paths
    )
    {
        var what = paths.Count == 1 ? paths[0] : $"{Paths(paths.Count)} in {target}";
        return new Notice(
            NoticeKind.Succeeded,
            $"Resolved {what}, keeping {KeptVersion.Of(kept)}",
            paths.Count == 1 ? null : string.Join('\n', paths),
            // The one resolution SVN does not read the file for, so markers can still be in it.
            kept is ConflictResolution.Working
                ? "Check for <<<<<<< before you commit — nothing looked inside the files."
                : null
        );
    }

    private static Notice Refused(string target, ConflictResolution kept, ResolveResponse response)
    {
        var headline =
            response.ResolvedPaths.Count == 0
                ? $"{target} was not resolved"
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Resolved {Paths(response.ResolvedPaths.Count)}; {response.Refusals.Count:N0} refused"
                );
        return new Notice(
            NoticeKind.NeedsAttention,
            headline,
            string.Join('\n', response.Refusals),
            // What a person hits: a tree conflict asked to become anything but the working version.
            kept is ConflictResolution.Working
                ? null
                : "A tree conflict can only be marked as resolved, once it is settled by hand."
        );
    }

    private static string Paths(int count) =>
        count == 1 ? "1 path" : string.Create(CultureInfo.InvariantCulture, $"{count:N0} paths");
}
