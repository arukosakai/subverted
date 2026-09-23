using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// Which BASE revisions a path and everything below it are at. Read out of wc.db, which answers
/// without a process; a wc.db whose schema this build does not understand is asked through
/// <c>svnversion</c> instead, so the answer never depends on the fast path alone (D1).
/// </summary>
public sealed class BaseRevisionRangeReader(SvnVersionCommand fallback)
{
    /// <param name="path">Absolute. Inside a directory external, that external's own copy answers.</param>
    /// <returns>The range, or null when nothing there has a BASE.</returns>
    /// <exception cref="WcDbException">No working copy at or above the path.</exception>
    /// <exception cref="SvnCommandException">The fallback was needed and failed.</exception>
    public async Task<BaseRevisionRange?> ReadAsync(
        string workingCopyRoot,
        string path,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var reader = WcDbReader.Open(path);
            return reader.ReadBaseRevisionRange(RelativeTo(reader.Info.RootPath, path));
        }
        catch (WcDbException ex) when (ex.Failure == WcDbFailure.Unreadable)
        {
            return await fallback.ReadAsync(workingCopyRoot, path, cancellationToken);
        }
    }

    /// <remarks>wc.db spells relative paths with <c>/</c> and the root as the empty string.</remarks>
    private static string RelativeTo(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative == "." ? string.Empty : relative.Replace(Path.DirectorySeparatorChar, '/');
    }
}
