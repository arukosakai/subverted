using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// Walks the nodes wc.db knows about, pairs each with its on-disk state, and reports whatever is
/// on disk that wc.db has never heard of as unversioned or ignored.
/// </summary>
/// <param name="globalIgnorePatterns">
/// Runtime-config patterns in force everywhere; <see cref="GlobalIgnoreConfiguration.Load"/>
/// supplies the ones this machine's <c>svn</c> would use. Passed in rather than read here so the
/// scan stays a function of its inputs.
/// </param>
public sealed class WorkingCopyScanner(
    WcDbReader reader,
    IReadOnlyList<string> globalIgnorePatterns
)
{
    private readonly PristineComparer _pristineComparer = new();

    public WorkingCopyInfo Info => reader.Info;

    /// <summary>
    /// Queued steps of an operation that was interrupted, which <c>svn cleanup</c> finishes. While
    /// any stand, <c>svn</c> refuses to read the working copy at all — <c>E155037</c>, and
    /// <c>svn status</c> is included in that.
    /// </summary>
    /// <remarks>
    /// Here rather than on the caller because the wc.db row shape is not this assembly's contract
    /// to publish, and because it shares the scan's one SQLite connection: putting both reads
    /// behind one object is what keeps them on one thread.
    /// </remarks>
    public int CountUnfinishedOperations() => reader.CountUnfinishedWork();

    /// <summary>
    /// Finds renames done outside SVN, by pairing each versioned file that is missing from disk with
    /// an unversioned file holding exactly the content wc.db recorded for it.
    /// </summary>
    /// <param name="entries">
    /// A listing this scanner produced. Taken as an argument rather than re-read so this costs no
    /// second tree walk — the missing and unversioned nodes are already decided by the time it runs.
    /// </param>
    /// <returns>The pairs, ordered by the path that went missing; empty when nothing went missing.</returns>
    /// <remarks>
    /// Content, not name: an artist who renames <c>hero.png</c> to <c>protagonist.png</c> shares no
    /// characters between the two, and a file overwritten by a different one of the same name shares
    /// all of them. Only a byte-exact match is claimed as a move.
    /// </remarks>
    public IReadOnlyList<UnrecordedMove> FindUnrecordedMoves(
        IReadOnlyList<WorkingCopyEntry> entries
    )
    {
        var missing = new List<FingerprintedNode>();
        var recordedSizes = new HashSet<long>();

        // Sequential: these share the scan's one SQLite connection. Only the hashing below is worth
        // spreading across cores, and only that is spread.
        foreach (var entry in entries)
        {
            if (entry is not { Status: NodeStatus.Missing, Kind: NodeKind.File })
            {
                continue;
            }

            // A translated working file never hashed to its recorded checksum in the first place
            // (see PristineComparer), so its copy at another path cannot be recognised either.
            if (
                reader.ReadNode(entry.RelPath) is not { IsTranslated: false } row
                || SvnChecksum.TryParseSha1(row.Checksum) is not { } digest
                || row.RecordedSize is not { } size
            )
            {
                continue;
            }

            missing.Add(new FingerprintedNode(entry.RelPath, digest));
            recordedSizes.Add(size);
        }

        return missing.Count == 0
            ? []
            : UnrecordedMoveResolver.Pair(missing, Fingerprint(SameSized(entries, recordedSizes)));
    }

    /// <summary>
    /// Unversioned files whose size on disk is one some missing node recorded.
    /// </summary>
    /// <remarks>
    /// The filter is what keeps this off the scan's critical path: hashing every unversioned file
    /// would make an ordinary working copy pay for whatever build output happens to sit in it, while
    /// a file of the wrong size cannot be the content of a missing one.
    /// </remarks>
    private List<string> SameSized(
        IReadOnlyList<WorkingCopyEntry> entries,
        IReadOnlySet<long> recordedSizes
    )
    {
        var candidates = new List<string>();
        foreach (var entry in entries)
        {
            if (entry is not { Status: NodeStatus.Unversioned, Kind: NodeKind.File })
            {
                continue;
            }

            if (
                WorkingCopyFileIndex.Snapshot(ToAbsolutePath(entry.RelPath)) is FileNode file
                && recordedSizes.Contains(file.Length)
            )
            {
                candidates.Add(entry.RelPath);
            }
        }

        return candidates;
    }

    /// <summary>Hashes the candidates across every core, dropping any that could not be read.</summary>
    private List<FingerprintedNode> Fingerprint(List<string> relPaths)
    {
        var digests = new string?[relPaths.Count];
        Parallel.For(
            0,
            relPaths.Count,
            i => digests[i] = WorkingFileDigest.TryCompute(ToAbsolutePath(relPaths[i]))
        );

        var fingerprinted = new List<FingerprintedNode>(relPaths.Count);
        for (var i = 0; i < relPaths.Count; i++)
        {
            if (digests[i] is { } digest)
            {
                fingerprinted.Add(new FingerprintedNode(relPaths[i], digest));
            }
        }

        return fingerprinted;
    }

    public IReadOnlyList<WorkingCopyEntry> Scan()
    {
        var entries = new List<WorkingCopyEntry>();
        var index = WorkingCopyFileIndex.Build(Info.RootPath);
        var versioned = new HashSet<string>(StringComparer.Ordinal);
        var undecided = new List<UndecidedNode>();
        var writeLocks = new WriteLockCoverage(reader.ReadWriteLocks());

        foreach (var row in reader.ReadNodes())
        {
            if (row.IsAbsentFromWorkingCopy)
            {
                continue;
            }

            versioned.Add(row.RelPath);

            // The metadata fast path answers most nodes. The ones it cannot prove clean are set
            // aside rather than hashed here, so the whole batch can be hashed at once below.
            var status = NodeStatusResolver.Resolve(row, index.Find(row.RelPath));
            if (status == NodeStatus.NeedsPristineCompare)
            {
                undecided.Add(new UndecidedNode(entries.Count, row));
            }

            entries.Add(
                new WorkingCopyEntry(
                    RelPath: row.RelPath,
                    Kind: row.Kind,
                    Status: status,
                    PropertyStatus: row.PropertyStatus,
                    Revision: row.BaseRevision,
                    Changelist: row.Changelist,
                    IsConflicted: row.HasConflict,
                    HasLockToken: row.HasLockToken,
                    IsWriteLocked: writeLocks.Reaches(row.Kind, row.RelPath),
                    IsCopied: CopyHistoryResolver.IsCopied(row, status)
                )
            );
        }

        SettleByContent(entries, undecided);
        var externals = AddExternalEntries(entries);
        AddUnversionedEntries(entries, index, versioned, externals);
        return entries;
    }

    /// <summary>
    /// Re-resolves just the paths that changed, instead of walking the tree again.
    /// </summary>
    /// <param name="held">The entry list from the last scan, returned unchanged where nothing moved.</param>
    /// <param name="changedRelPaths">Slash-separated, relative to the root.</param>
    /// <returns>
    /// The updated list, or <see langword="null"/> when the change cannot be applied this way and
    /// the caller must rescan. That is deliberately most of the interesting cases: a path the last
    /// scan does not already know as a versioned node changes the *set* of nodes, and the set is
    /// decided by ignore-rule inheritance and unversioned-subtree closing — whole-tree properties
    /// that cannot be recomputed from one path without listing files that should be hidden, or
    /// hiding files that should be listed.
    /// </returns>
    public IReadOnlyList<WorkingCopyEntry>? TryApplyChanges(
        IReadOnlyList<WorkingCopyEntry> held,
        IReadOnlySet<string> changedRelPaths
    )
    {
        if (changedRelPaths.Count == 0)
        {
            return held;
        }

        var targets = new List<int>(changedRelPaths.Count);
        for (var i = 0; i < held.Count; i++)
        {
            if (changedRelPaths.Contains(held[i].RelPath))
            {
                targets.Add(i);
            }
        }

        // A path the held scan has never heard of is a new node; one it knows as unversioned,
        // ignored or external is not something wc.db can re-answer on its own. Either way, rescan.
        if (targets.Count != changedRelPaths.Count)
        {
            return null;
        }

        // wc.db is what refuses the rest, and it needs no help: a path the scan holds as
        // unversioned, ignored or external has no NODES row by definition, so the read below
        // returns nothing for it. Checking the held status first would be a second guard that
        // can never fire.
        //
        // Sequential on purpose: these share one SQLite connection, which is not safe to query
        // from several threads. Only the hashing below is worth parallelising anyway.
        var rows = new WcDbRow[targets.Count];
        for (var target = 0; target < targets.Count; target++)
        {
            if (
                reader.ReadNode(held[targets[target]].RelPath) is not { } row
                || row.IsAbsentFromWorkingCopy
            )
            {
                return null;
            }

            rows[target] = row;
        }

        // Read before the fan-out for the same reason the rows are: one SQLite connection.
        var writeLocks = new WriteLockCoverage(reader.ReadWriteLocks());

        var resolved = new WorkingCopyEntry[targets.Count];
        Parallel.For(
            0,
            targets.Count,
            target => resolved[target] = Resolve(rows[target], writeLocks)
        );

        var updated = held.ToArray();
        for (var target = 0; target < targets.Count; target++)
        {
            updated[targets[target]] = resolved[target];
        }

        return updated;
    }

    /// <summary>One node, resolved the same way <see cref="Scan"/> resolves it.</summary>
    private WorkingCopyEntry Resolve(WcDbRow row, WriteLockCoverage writeLocks)
    {
        var absolutePath = ToAbsolutePath(row.RelPath);
        var status = NodeStatusResolver.Resolve(row, WorkingCopyFileIndex.Snapshot(absolutePath));
        if (status == NodeStatus.NeedsPristineCompare)
        {
            status = _pristineComparer.Compare(row, absolutePath);
        }

        return new WorkingCopyEntry(
            RelPath: row.RelPath,
            Kind: row.Kind,
            Status: status,
            PropertyStatus: row.PropertyStatus,
            Revision: row.BaseRevision,
            Changelist: row.Changelist,
            IsConflicted: row.HasConflict,
            HasLockToken: row.HasLockToken,
            IsWriteLocked: writeLocks.Reaches(row.Kind, row.RelPath),
            IsCopied: CopyHistoryResolver.IsCopied(row, status)
        );
    }

    /// <summary>
    /// Externals are a working copy of their own sitting inside this one: no NODES row, their own
    /// <c>.svn</c>, and <c>svn status</c> reports the root as <c>X</c> without folding the contents
    /// into this listing.
    /// </summary>
    /// <returns>
    /// Their paths, which the unversioned pass must treat as both claimed and closed — otherwise
    /// the external reads as a directory to add, and everything inside it as unversioned files.
    /// </returns>
    private IReadOnlySet<string> AddExternalEntries(List<WorkingCopyEntry> entries)
    {
        var externals = reader.ReadExternals();
        foreach (var external in externals)
        {
            entries.Add(
                new WorkingCopyEntry(
                    RelPath: external.RelPath,
                    Kind: external.Kind,
                    Status: NodeStatus.External,
                    PropertyStatus: PropertyStatus.Unmodified,
                    // svn's XML carries no revision on the external placeholder: the revision that
                    // would mean anything belongs to the other working copy, not to this one.
                    Revision: null,
                    Changelist: null,
                    IsConflicted: false,
                    // An external is its own working copy with its own wc.db, so a write lock on it
                    // is not a row in ours and is not ours to report.
                    HasLockToken: false,
                    IsWriteLocked: false,
                    IsCopied: false
                )
            );
        }

        return externals.Select(external => external.RelPath).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Hashes the nodes the metadata fast path could not settle, across every core.
    /// </summary>
    /// <remarks>
    /// On a working copy whose recorded mtimes have all moved — the state any real session of work
    /// leaves behind — this is where a 100k-node scan spends 19 of its 20 seconds, one file open at
    /// a time. Each node's answer depends on nothing but its own row and its own bytes, so the only
    /// reason it was sequential was that it started out as one loop.
    ///
    /// Statuses land in their own array first: <see cref="List{T}"/> promises nothing about
    /// concurrent writes, even to distinct indices.
    /// </remarks>
    private void SettleByContent(List<WorkingCopyEntry> entries, List<UndecidedNode> undecided)
    {
        if (undecided.Count == 0)
        {
            return;
        }

        var settled = new NodeStatus[undecided.Count];
        Parallel.For(
            0,
            undecided.Count,
            i =>
                settled[i] = _pristineComparer.Compare(
                    undecided[i].Row,
                    ToAbsolutePath(undecided[i].Row.RelPath)
                )
        );

        for (var i = 0; i < undecided.Count; i++)
        {
            var at = undecided[i].EntryIndex;
            entries[at] = entries[at] with { Status = settled[i] };
        }
    }

    private void AddUnversionedEntries(
        List<WorkingCopyEntry> entries,
        WorkingCopyFileIndex index,
        HashSet<string> versioned,
        IReadOnlySet<string> externals
    )
    {
        var ignoreRules = WorkingCopyIgnoreRules.Build(
            reader.ReadDirectoryIgnorePatterns(),
            globalIgnorePatterns
        );

        // SVN reports an unversioned directory once and never lists what is inside it, ignored or
        // not — so every unversioned directory closes its whole subtree. An external closes its
        // own for the same reason, and is already listed.
        var closedSubtrees = new HashSet<string>(externals, StringComparer.Ordinal);

        foreach (var (relPath, snapshot) in index.InTreeOrder())
        {
            if (versioned.Contains(relPath) || externals.Contains(relPath))
            {
                continue;
            }

            var isDirectory = snapshot is DirectoryNode;
            if (isDirectory)
            {
                closedSubtrees.Add(relPath);
            }

            var separator = relPath.LastIndexOf('/');
            var parent = separator < 0 ? string.Empty : relPath[..separator];
            if (closedSubtrees.Contains(parent))
            {
                continue;
            }

            var name = separator < 0 ? relPath : relPath[(separator + 1)..];
            entries.Add(
                new WorkingCopyEntry(
                    RelPath: relPath,
                    Kind: isDirectory ? NodeKind.Directory : NodeKind.File,
                    Status: ignoreRules.IsIgnored(parent, name)
                        ? NodeStatus.Ignored
                        : NodeStatus.Unversioned,
                    PropertyStatus: PropertyStatus.Unmodified,
                    Revision: null,
                    Changelist: null,
                    IsConflicted: false,
                    HasLockToken: false,
                    IsWriteLocked: false,
                    IsCopied: false
                )
            );
        }
    }

    private string ToAbsolutePath(string relPath) =>
        Path.Combine(Info.RootPath, relPath.Replace('/', Path.DirectorySeparatorChar));

    /// <param name="EntryIndex">Where the node already sits in the entry list, awaiting its verdict.</param>
    private readonly record struct UndecidedNode(int EntryIndex, WcDbRow Row);
}
