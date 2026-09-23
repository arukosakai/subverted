using System.IO.Enumeration;

namespace Subverted.Svn;

/// <summary>
/// On-disk state for every path under a working copy, captured in one tree walk.
/// </summary>
internal sealed class WorkingCopyFileIndex
{
    private const string AdminDirectory = ".svn";

    // Stateless and compared by value, so one instance serves every directory in the tree.
    private static readonly DirectoryNode Directory = new();

    private readonly Dictionary<string, NodeSnapshot> _snapshots;

    private WorkingCopyFileIndex(Dictionary<string, NodeSnapshot> snapshots) =>
        _snapshots = snapshots;

    /// <summary>
    /// Size and write time come from the directory enumeration itself, so the cost is one
    /// enumeration per directory rather than one stat per file. On a 20k-file working copy that
    /// distinction is worth roughly a second.
    /// </summary>
    public static WorkingCopyFileIndex Build(string rootPath)
    {
        var snapshots = new Dictionary<string, NodeSnapshot>(StringComparer.Ordinal);
        if (!System.IO.Directory.Exists(rootPath))
        {
            return new WorkingCopyFileIndex(snapshots);
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var prefixLength = root.Length + 1;

        // The enumeration only yields what is *below* the root, but wc.db has a node for the root
        // itself under the empty relative path, and it needs a snapshot like any other directory.
        snapshots[string.Empty] = Directory;

        var enumeration = new FileSystemEnumerable<Entry>(root, Transform, EnumerationOptions)
        {
            ShouldRecursePredicate = static (ref FileSystemEntry entry) =>
                !entry.FileName.SequenceEqual(AdminDirectory),
        };

        foreach (var entry in enumeration)
        {
            var relPath = entry.FullPath[prefixLength..].Replace('\\', '/');

            // The enumeration does not descend into an admin directory but still yields the
            // directory itself. There is more than one once an external is checked out, and a
            // stray `.svn` in the listing reads as something to add.
            if (IsAdminDirectory(relPath))
            {
                continue;
            }

            snapshots[relPath] = entry.IsDirectory
                ? Directory
                : new FileNode(entry.Length, entry.LastWriteTimeUtc);
        }

        return new WorkingCopyFileIndex(snapshots);
    }

    private static bool IsAdminDirectory(string relPath) =>
        relPath == AdminDirectory
        || relPath.EndsWith($"/{AdminDirectory}", StringComparison.Ordinal);

    /// <summary>
    /// On-disk state for one path, for re-resolving a single node without walking the tree.
    /// </summary>
    /// <remarks>
    /// Must answer exactly as <see cref="Build"/> does for the same path, or an incremental update
    /// and a full rescan disagree about the same file. <see cref="FileInfo.Exists"/> is false for a
    /// directory, so the directory case is asked separately rather than inferred from attributes.
    /// </remarks>
    /// <returns><see langword="null"/> when nothing exists there.</returns>
    public static NodeSnapshot? Snapshot(string absolutePath)
    {
        var file = new FileInfo(absolutePath);
        if (file.Exists)
        {
            return new FileNode(file.Length, file.LastWriteTimeUtc);
        }

        return System.IO.Directory.Exists(absolutePath) ? Directory : null;
    }

    /// <param name="relPath">Slash-separated, as stored in wc.db's <c>local_relpath</c>.</param>
    /// <returns><see langword="null"/> when nothing exists at that path.</returns>
    public NodeSnapshot? Find(string relPath) => _snapshots.GetValueOrDefault(relPath);

    /// <summary>
    /// Every path on disk, ordered so a directory always precedes its contents — a prefix sorts
    /// before the strings that extend it, which is what lets a caller suppress a whole subtree the
    /// moment it sees the directory.
    /// </summary>
    public IEnumerable<KeyValuePair<string, NodeSnapshot>> InTreeOrder() =>
        _snapshots.OrderBy(snapshot => snapshot.Key, StringComparer.Ordinal);

    private static EnumerationOptions EnumerationOptions =>
        new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
        };

    private static Entry Transform(ref FileSystemEntry entry) =>
        new(
            Path.Join(entry.Directory, entry.FileName),
            entry.IsDirectory,
            entry.IsDirectory ? 0L : entry.Length,
            entry.LastWriteTimeUtc.UtcDateTime
        );

    private readonly record struct Entry(
        string FullPath,
        bool IsDirectory,
        long Length,
        DateTime LastWriteTimeUtc
    );
}
