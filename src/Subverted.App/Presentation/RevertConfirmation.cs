using System.Globalization;
using Subverted.Core;

namespace Subverted.App.Presentation;

/// <summary>
/// What the person is asked before a revert: the exact list of what it touches, since the daemon
/// does not ask (D19) and nothing it throws away comes back.
/// </summary>
/// <param name="Target">The row being reverted, relative to the root.</param>
/// <param name="Lines">
/// Every listed path the revert reaches — the row, and for a directory everything beneath it, as
/// revert goes to infinite depth — in path order. Empty when it would do nothing.
/// </param>
public sealed record RevertConfirmation(string Target, IReadOnlyList<RevertLine> Lines)
{
    public string Title =>
        Lines.Count == 1
            ? $"Revert {Lines[0].RelPath}?"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Revert {Lines.Count:N0} paths in {Target}?"
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
                (0, _) => "Nothing here is lost: it is only put back or un-scheduled.",
                (1, 1) => "What it throws away cannot be brought back.",
                (1, _) => "1 of these loses work that cannot be brought back.",
                _ => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{losing:N0} of these lose work that cannot be brought back."
                ),
            };
        }
    }

    /// <param name="target">A change row; revert is not offered on a folder that only holds others.</param>
    /// <param name="listed">The whole listing, whatever the filter shows: revert does not see the filter.</param>
    public static RevertConfirmation For(ChangeRow target, IEnumerable<ChangeRow> listed) =>
        For(target.RelPath, listed);

    /// <param name="target">
    /// Any path relative to the root, a folder with no line of its own included. The listing must
    /// reach everything beneath it, or the list would leave out what the revert reaches.
    /// </param>
    /// <param name="listed">The whole listing, whatever the filter shows: revert does not see the filter.</param>
    public static RevertConfirmation For(string target, IEnumerable<ChangeRow> listed)
    {
        var lines = listed
            .Select(RevertLoss.For)
            .OfType<RevertLine>()
            .Where(line => TargetCoverage.Covers(target, line.RelPath, Ordinal))
            .OrderBy(line => line.RelPath, StringComparer.Ordinal)
            .ToList();
        return new RevertConfirmation(target, lines);
    }

    /// <summary>Every path here is the daemon's own spelling within one listing.</summary>
    private const StringComparison Ordinal = StringComparison.Ordinal;
}
