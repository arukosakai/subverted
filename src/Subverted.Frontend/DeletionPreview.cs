using System.Globalization;
using Subverted.Core;

namespace Subverted.Frontend;

/// <summary>
/// What the person reads before a delete: every node it reaches and what becomes of each, since the
/// daemon does not ask and <c>svn delete --force</c> names nothing it removes without a pristine.
/// </summary>
/// <param name="Target">The node being deleted, relative to the root.</param>
/// <param name="Lines">
/// The target and everything beneath it, less what is already marked deleted: what is lost for good
/// first, then the rest, each in path order.
/// </param>
public sealed record DeletionPreview(string Target, IReadOnlyList<DeletionLine> Lines)
{
    public string Title =>
        Lines.Count == 1
            ? $"Delete {Lines[0].RelPath}?"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Delete {Lines.Count:N0} paths in {Target}?"
            );

    /// <summary>Whether any line loses work for good, which is what the warning is drawn as.</summary>
    public bool LosesWork => Lines.Any(line => line.LosesWork);

    /// <summary>What is lost for good, or that nothing is; said plainly above the list.</summary>
    public string Warning
    {
        get
        {
            var losing = Lines.Count(line => line.LosesWork);
            return (losing, Lines.Count) switch
            {
                (0, _) => "Nothing here is lost: SVN still has all of it.",
                (1, 1) => "What it deletes cannot be brought back.",
                (1, _) => "1 of these loses work that cannot be brought back.",
                _ => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{losing:N0} of these lose work that cannot be brought back."
                ),
            };
        }
    }

    /// <param name="target">The picked node, relative to the root.</param>
    /// <param name="listing">
    /// A listing with unmodified and ignored nodes in it: a delete takes both, and a listing of
    /// changes would leave out exactly the ignored files it removes with no pristine behind them.
    /// </param>
    /// <param name="comparison">How this platform compares paths.</param>
    public static DeletionPreview Of(
        string target,
        IEnumerable<WorkingCopyEntry> listing,
        StringComparison comparison
    )
    {
        var lines = listing
            .Where(entry => TargetCoverage.Covers(target, entry.RelPath, comparison))
            .Select(DeletionLoss.For)
            .OfType<DeletionLine>()
            .OrderByDescending(line => line.LosesWork)
            .ThenBy(line => line.RelPath, StringComparer.Ordinal)
            .ToList();
        return new DeletionPreview(target, lines);
    }

    /// <summary>
    /// The same target with the same lines saying the same things. Anything else means the working
    /// copy changed under the question, and what was confirmed is not what would be deleted.
    /// </summary>
    public bool IsSameQuestionAs(DeletionPreview other) =>
        Target == other.Target && Lines.SequenceEqual(other.Lines);
}
