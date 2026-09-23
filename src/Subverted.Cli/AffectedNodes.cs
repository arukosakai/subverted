using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Cli;

/// <summary>
/// The nodes in a status listing that lie under the paths a command was given and match that
/// command's idea of "at risk". Pure: this is the selection behind every list someone reads in the
/// second before they lose a morning, so it is one tested rule rather than one per command.
/// </summary>
public static class AffectedNodes
{
    /// <param name="status">A listing of the whole working copy the targets are in.</param>
    /// <param name="paths">Absolute targets, as the daemon will be given them.</param>
    /// <param name="comparison">
    /// How this platform compares paths. Taken as an argument so both answers are covered by tests
    /// on either operating system.
    /// </param>
    /// <param name="isAtRisk">
    /// What this particular command would disturb. The caller owns it because the answer genuinely
    /// differs — a revert passes over an unversioned file, a resolve passes over everything that is
    /// not conflicted.
    /// </param>
    /// <returns>The matching entries, in the order the daemon reported them.</returns>
    public static IReadOnlyList<WorkingCopyEntry> Under(
        StatusResponse status,
        IReadOnlyList<string> paths,
        StringComparison comparison,
        Func<WorkingCopyEntry, bool> isAtRisk
    )
    {
        var targets = paths
            .Select(path => TargetCoverage.RelativeTo(status.Info.RootPath, path))
            .ToList();

        return
        [
            .. status
                .Entries.Where(isAtRisk)
                .Where(entry =>
                    targets.Any(target => TargetCoverage.Covers(target, entry.RelPath, comparison))
                ),
        ];
    }
}
