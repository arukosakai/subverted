namespace Subverted.App.Presentation;

/// <summary>
/// Which changes the person has ticked, kept by path beside the rows rather than on them, so the
/// once-a-second resync — which replaces any row that changed — never clears a tick.
/// </summary>
/// <remarks>
/// A path gets its <see cref="DefaultTick"/> once, when it first appears; after that only the
/// person changes it, so an untick survives every resync that still lists the path.
/// </remarks>
public sealed class TickedPaths
{
    private readonly HashSet<string> _paths = new(StringComparer.Ordinal);

    /// <summary>Every listed path, and whether it was last seen as a rename row.</summary>
    private readonly Dictionary<string, bool> _seenAsRename = new(StringComparer.Ordinal);

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

    /// <summary>Unticks these paths, as after they were committed.</summary>
    public void Untick(IEnumerable<string> relPaths) => _paths.ExceptWith(relPaths);

    /// <summary>
    /// Brings the ticks up to date with a fresh listing: a path it no longer has is forgotten, so
    /// if it comes back it is a new change with its default again; a path appearing for the first
    /// time takes its default; a path already seen keeps whatever the person left it as.
    /// </summary>
    /// <remarks>
    /// A path that turns into a rename row counts as new: the unversioned half of a rename can be
    /// listed a scan before its missing half, and then the pair must still start ticked.
    /// </remarks>
    /// <param name="listed">Every row in the fresh listing — not only what the filter shows.</param>
    public void Follow(IEnumerable<ChangeRow> listed)
    {
        var rows = listed.ToList();
        var paths = rows.Select(row => row.RelPath).ToHashSet(StringComparer.Ordinal);
        _paths.IntersectWith(paths);
        foreach (var gone in _seenAsRename.Keys.Where(path => !paths.Contains(path)).ToList())
        {
            _seenAsRename.Remove(gone);
        }

        foreach (var row in rows)
        {
            var isRename = row.RenamedFrom is not null;
            var isNew =
                !_seenAsRename.TryGetValue(row.RelPath, out var wasRename)
                || (isRename && !wasRename);
            _seenAsRename[row.RelPath] = isRename;
            if (isNew && DefaultTick.For(row))
            {
                _paths.Add(row.RelPath);
            }
        }
    }
}
