using Subverted.Core;
using Subverted.Frontend;
using Subverted.Protocol;

namespace Subverted.Cli;

/// <summary>
/// What a removal would take, and whether it may be sent at all. Pure, and every rule in it is the
/// app's: <see cref="DeletionOffer"/> decides what is refused and <see cref="DeletionLoss"/> what is
/// lost for good, so <c>sv rm</c> and the Delete menu cannot tell a person two different things.
/// </summary>
/// <param name="Refusals">
/// Why a named target may not be deleted, one sentence each. Any at all means nothing is sent.
/// </param>
/// <param name="Recoverable">Nodes SVN still has, which Revert or a copy's source brings back.</param>
/// <param name="Unrecoverable">
/// Nodes that take something only this working copy had: an edit, an add, a file SVN does not
/// track, an external's contents.
/// </param>
public sealed record RemovalPreview(
    IReadOnlyList<string> Refusals,
    IReadOnlyList<RemovedNode> Recoverable,
    IReadOnlyList<RemovedNode> Unrecoverable
)
{
    /// <summary>
    /// The listing this preview has to be built from.
    /// </summary>
    /// <remarks>
    /// Both switches are on, and neither is optional. A file nobody has edited is the commonest
    /// thing anyone removes and is absent from a default listing entirely — asking for one would
    /// make <c>sv rm</c> answer "nothing to remove" and do nothing, which is how it behaved until
    /// somebody ran it. An ignored file is hidden for the opposite reason and is about to be
    /// unlinked with no pristine behind it.
    /// </remarks>
    public static StatusRequest ListingFor(string path) =>
        new(path, IncludeUnmodified: true, IncludeIgnored: true);

    /// <param name="status">A listing of the whole working copy the targets are in.</param>
    /// <param name="paths">Absolute removal targets, as the daemon will be given them.</param>
    /// <param name="comparison">
    /// How this platform compares paths. Taken as an argument so both answers are covered by tests
    /// on either operating system.
    /// </param>
    /// <remarks>A target the listing does not hold is neither refused nor reached.</remarks>
    public static RemovalPreview Of(
        StatusResponse status,
        IReadOnlyList<string> paths,
        StringComparison comparison
    )
    {
        var refusals = paths
            .Select(path => TargetCoverage.RelativeTo(status.Info.RootPath, path))
            .Select(target =>
                status.Entries.FirstOrDefault(entry => entry.RelPath.Equals(target, comparison))
            )
            .OfType<WorkingCopyEntry>()
            .Select(target => DeletionOffer.RefusalFor(target, status.Entries, comparison))
            .OfType<string>()
            .ToList();

        var reached = AffectedNodes
            .Under(status, paths, comparison, _ => true)
            .Select(entry => DeletionLoss.For(entry) is { } loss ? new RemovedNode(entry, loss) : null)
            .OfType<RemovedNode>()
            .ToList();

        return new(
            refusals,
            [.. reached.Where(node => !node.Loss.LosesWork)],
            [.. reached.Where(node => node.Loss.LosesWork)]
        );
    }

    public int Count => Recoverable.Count + Unrecoverable.Count;

    /// <summary>
    /// The nodes in <c>sv st</c>'s own layout, with the unrecoverable ones called out below and each
    /// saying what it loses.
    /// </summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            var lines = new List<string>(Recoverable.Select(node => StatusLine.Compact(node.Entry)));
            if (Unrecoverable.Count == 0)
            {
                return lines;
            }

            if (lines.Count > 0)
            {
                lines.Add(string.Empty);
            }

            lines.Add(
                Unrecoverable.Count == 1
                    ? "1 of these loses work that cannot be brought back:"
                    : $"{Unrecoverable.Count} of these lose work that cannot be brought back:"
            );
            lines.AddRange(
                Unrecoverable.Select(node => $"{StatusLine.Compact(node.Entry)}: {node.Loss.What}")
            );
            return lines;
        }
    }
}
