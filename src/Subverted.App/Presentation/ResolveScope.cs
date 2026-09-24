using System.Globalization;
using Subverted.Core;

namespace Subverted.App.Presentation;

/// <summary>
/// The conflicts a resolve reaches, and the question put first when it replaces the person's own
/// version — the exact list, since the version it throws away exists nowhere else.
/// </summary>
/// <param name="Target">The row being resolved, relative to the root.</param>
/// <param name="Lines">
/// Every listed conflicted path the resolve reaches — the row, and for a directory every conflict
/// beneath it, since resolve goes to infinite depth — in path order. Empty when it would do nothing.
/// </param>
public sealed record ResolveScope(
    string Target,
    ConflictResolution Resolution,
    IReadOnlyList<string> Lines
)
{
    public string Title =>
        Lines.Count == 1
            ? $"Keep {KeptVersion.Of(Resolution)} of {Lines[0]}?"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Keep {KeptVersion.Of(Resolution)} of {Lines.Count:N0} conflicted paths in {Target}?"
            );

    public string Warning =>
        Lines.Count == 1
            ? "Your version is thrown away and cannot be brought back."
            : "Your version of each is thrown away and cannot be brought back.";

    /// <param name="target">A change row; resolve is not offered on a folder that only holds others.</param>
    /// <param name="listed">The whole listing, whatever the filter shows: resolve does not see the filter.</param>
    public static ResolveScope For(
        ChangeRow target,
        ConflictResolution resolution,
        IEnumerable<ChangeRow> listed
    ) =>
        new(
            target.RelPath,
            resolution,
            [
                .. listed
                    .Where(row => row.Entry.IsConflicted)
                    .Select(row => row.RelPath)
                    .Where(path => TargetCoverage.Covers(target.RelPath, path, Ordinal))
                    .Order(StringComparer.Ordinal),
            ]
        );

    /// <summary>Every path here is the daemon's own spelling within one listing.</summary>
    private const StringComparison Ordinal = StringComparison.Ordinal;
}
