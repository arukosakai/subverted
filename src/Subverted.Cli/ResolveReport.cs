using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Cli;

/// <summary>
/// What a resolve did, as the lines to print. The count is the point: <c>svn resolve</c> says
/// nothing and exits zero for a path it had nothing to do for, so without it "resolved" and "found
/// nothing to resolve" look identical.
/// </summary>
public static class ResolveReport
{
    public static IReadOnlyList<string> Lines(ResolveResponse response, ConflictResolution kept) =>
        [
            .. response.ResolvedPaths.Select(path => $"resolved  {path}"),
            .. response.Refusals,
            .. Summary(response, kept),
        ];

    private static IEnumerable<string> Summary(ResolveResponse response, ConflictResolution kept)
    {
        if (response.ResolvedPaths.Count == 0 && response.Refusals.Count == 0)
        {
            yield return "nothing was conflicted";
            yield break;
        }

        if (response.ResolvedPaths.Count > 0)
        {
            yield return $"{response.ResolvedPaths.Count} node(s) resolved, keeping {Kept(kept)}";
        }

        // Keeping the file as it stands is the one resolution SVN does not read the file to check,
        // so it will happily call a node finished with the merge markers still in it.
        if (kept is ConflictResolution.Working && response.ResolvedPaths.Count > 0)
        {
            yield return "check them for <<<<<<< before you commit — nothing above looked inside the files.";
        }

        if (response.Refusals.Count > 0)
        {
            yield return $"{response.Refusals.Count} node(s) were NOT resolved — see the warning(s) above.";
        }
    }

    private static string Kept(ConflictResolution kept) =>
        kept switch
        {
            ConflictResolution.Working => "the files as they are on disk",
            ConflictResolution.Mine => "this working copy's version",
            ConflictResolution.Theirs => "the incoming version",
            ConflictResolution.Base => "the revision both sides started from",
            _ => throw new ArgumentOutOfRangeException(nameof(kept)),
        };
}
