namespace Subverted.App.Presentation;

/// <summary>
/// Which changes the person has ticked, kept by path beside the rows rather than on them, so the
/// once-a-second resync — which replaces any row that changed — never clears a tick.
/// </summary>
public sealed class TickedPaths
{
    private readonly HashSet<string> _paths = new(StringComparer.Ordinal);

    public IReadOnlySet<string> Paths => _paths;

    public bool IsTicked(string relPath) => _paths.Contains(relPath);

    /// <returns>Whether the path is ticked afterwards.</returns>
    public bool Toggle(string relPath)
    {
        if (_paths.Remove(relPath))
        {
            return false;
        }

        _paths.Add(relPath);
        return true;
    }

    /// <summary>
    /// Forgets ticks on paths the listing no longer has. A change committed from elsewhere that
    /// later comes back is a new change, and must not come back already ticked.
    /// </summary>
    /// <param name="listed">Every path in the fresh listing — not only what the filter shows.</param>
    public void KeepOnly(IEnumerable<string> listed) => _paths.IntersectWith(listed);
}
