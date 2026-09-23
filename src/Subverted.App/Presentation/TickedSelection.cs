using Subverted.Core;
using Subverted.Frontend;

namespace Subverted.App.Presentation;

/// <summary>
/// What a commit of the ticked rows would send, once D20's directory rules have had their say
/// (<see cref="DecidedSubtrees"/>, the same copy <c>sv pick</c> obeys).
/// </summary>
/// <param name="Sent">The ticked rows that are a choice, ordered by path. A rename is one row.</param>
/// <param name="DecidedByFolder">
/// Paths a directory above them has already settled — left out of an added directory that is not
/// sent, a deletion carried by its deleted directory, or anything beneath a missing directory that
/// is sent. Their tick is not a choice, so it is
/// neither shown as one nor sent.
/// </param>
public sealed record TickedSelection(
    IReadOnlyList<ChangeRow> Sent,
    IReadOnlySet<string> DecidedByFolder
)
{
    public static readonly TickedSelection Nothing = new([], new HashSet<string>());

    /// <summary>
    /// The paths a <c>CommitSelectionRequest</c> names, relative to the root. A rename names both
    /// halves, old path first, since the daemon refuses half a pair.
    /// </summary>
    public IReadOnlyList<string> RelPaths =>
        [
            .. Sent.SelectMany(row =>
                row.RenamedFrom is { } from ? [from, row.RelPath] : new[] { row.RelPath }
            ),
        ];

    /// <param name="listed">
    /// Every listed row, whatever the filter shows: a hidden directory still decides what lies
    /// beneath it.
    /// </param>
    /// <param name="ticked">The ticked paths.</param>
    /// <param name="shown">
    /// The paths the filter lets through. A tick the filter hides is kept but not sent (operator's
    /// call), so a commit never includes a change the person cannot see.
    /// </param>
    public static TickedSelection Of(
        IEnumerable<ChangeRow> listed,
        IReadOnlySet<string> ticked,
        IReadOnlySet<string> shown
    )
    {
        // Every path here is the daemon's own spelling within one listing, so ordinal is exact.
        var decisions = new DecidedSubtrees(StringComparison.Ordinal);
        List<string> sentMissingFolders = [];
        List<ChangeRow> sent = [];
        HashSet<string> decided = new(StringComparer.Ordinal);

        // Ordinal order puts every ancestor before what lies beneath it, as the rules require.
        foreach (var row in listed.OrderBy(row => row.RelPath, StringComparer.Ordinal))
        {
            if (
                decisions.Decides(row.Entry)
                || sentMissingFolders.Any(folder =>
                    TargetCoverage.Below(folder, row.RelPath, Ordinal)
                )
            )
            {
                decided.Add(row.RelPath);
            }
            else if (ticked.Contains(row.RelPath) && shown.Contains(row.RelPath))
            {
                decisions.Sent(row.Entry);
                if (IsMissingFolder(row.Entry))
                {
                    sentMissingFolders.Add(row.RelPath);
                }

                sent.Add(row);
            }
            else
            {
                decisions.Left(row.Entry);
            }
        }

        return new TickedSelection(sent, decided);
    }

    private const StringComparison Ordinal = StringComparison.Ordinal;

    /// <summary>
    /// Measured on 1.8.15, and not yet in <see cref="DecidedSubtrees"/>: recording a missing folder's
    /// deletion records its whole subtree, so once it is sent nothing beneath it is a choice; left
    /// alone, each missing child can still be deleted on its own.
    /// </summary>
    private static bool IsMissingFolder(WorkingCopyEntry entry) =>
        entry.Kind == NodeKind.Directory && entry.Status == NodeStatus.Missing;
}
