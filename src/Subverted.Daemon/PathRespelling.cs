namespace Subverted.Daemon;

/// <summary>
/// Respells a path through its longest existing ancestor. Only a path that exists can be asked how
/// the filesystem spells it, and a request routinely names one that does not: a move's
/// destination, a file deleted from disk. Pure: the filesystem is handed in.
/// </summary>
public static class PathRespelling
{
    /// <param name="fullPath">Absolute and already normalised by <see cref="Path.GetFullPath(string)"/>.</param>
    /// <param name="exists">Whether a path is there to be asked about.</param>
    /// <param name="spellExisting">The filesystem's own spelling of a path that exists.</param>
    /// <returns>The existing part respelled, and whatever lay beyond it appended as given.</returns>
    public static string Respell(
        string fullPath,
        Func<string, bool> exists,
        Func<string, string> spellExisting
    )
    {
        var existing = fullPath;
        var beyond = new Stack<string>();
        while (!exists(existing))
        {
            if (Path.GetDirectoryName(existing) is not { } parent)
            {
                return fullPath;
            }

            beyond.Push(Path.GetFileName(existing));
            existing = parent;
        }

        return beyond.Aggregate(spellExisting(existing), Path.Combine);
    }
}
