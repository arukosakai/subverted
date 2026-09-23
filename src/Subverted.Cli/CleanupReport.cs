using Subverted.Protocol;

namespace Subverted.Cli;

/// <summary>
/// What a cleanup did, as the lines to print. Every line here is computed rather than passed
/// through, because <c>svn cleanup</c> writes nothing on any outcome — "unwedged a stuck working
/// copy" and "found nothing wrong" are the same empty output and the same exit code.
/// </summary>
public static class CleanupReport
{
    public static IReadOnlyList<string> Lines(CleanupResponse response) =>
        [
            .. response.ReleasedWriteLocks.Select(path => $"released  {Display(path)}"),
            .. Summary(response),
        ];

    private static IEnumerable<string> Summary(CleanupResponse response)
    {
        if (
            response.ReleasedWriteLocks.Count == 0
            && response.FinishedOperations == 0
            && response.RemainingWriteLocks.Count == 0
        )
        {
            yield return "nothing needed cleaning";
            yield break;
        }

        if (response.ReleasedWriteLocks.Count > 0)
        {
            yield return $"{response.ReleasedWriteLocks.Count} path(s) unlocked — "
                + "writes to this working copy will work again";
        }

        // Worth its own line rather than folding into the count above: this is the state in which
        // `svn status` itself was refusing to answer, so the user has been unable to see anything.
        if (response.FinishedOperations > 0)
        {
            yield return $"{response.FinishedOperations} interrupted operation(s) finished";
        }

        if (response.RemainingWriteLocks.Count > 0)
        {
            yield return $"{response.RemainingWriteLocks.Count} path(s) are STILL locked — "
                + "the working copy is not fixed. Check that no other SVN client is running.";
        }
    }

    /// <summary>The working-copy root has an empty relative path; <c>svn status</c> calls it <c>.</c></summary>
    private static string Display(string relPath) => relPath.Length == 0 ? "." : relPath;
}
