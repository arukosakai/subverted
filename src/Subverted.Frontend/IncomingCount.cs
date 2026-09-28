using Subverted.Core;

namespace Subverted.Frontend;

/// <summary>
/// How many things an update would change, counted the way a person would count them: SVN also
/// marks every folder whose list of children changed, and counting that folder beside the child
/// that caused it would say two where one happened.
/// </summary>
public static class IncomingCount
{
    /// <returns>
    /// Every marked path with nothing marked beneath it, plus any folder whose own properties
    /// changed — that change is the folder's, not its children's.
    /// </returns>
    public static int Of(IReadOnlyList<IncomingChange> changes)
    {
        var holdingMarks = changes
            .SelectMany(change => AncestorsOf(change.RelPath))
            .ToHashSet(StringComparer.Ordinal);

        return changes.Count(change =>
            change.PropertiesChanged || !holdingMarks.Contains(change.RelPath)
        );
    }

    /// <remarks>The root is the empty string, so it is an ancestor of every other path.</remarks>
    private static IEnumerable<string> AncestorsOf(string relPath)
    {
        if (relPath.Length == 0)
        {
            yield break;
        }

        for (
            var slash = relPath.LastIndexOf('/');
            slash > 0;
            slash = relPath.LastIndexOf('/', slash - 1)
        )
        {
            yield return relPath[..slash];
        }

        yield return string.Empty;
    }
}
