using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.App.Tests;

/// <summary>Daemon answers for tests, with boring defaults so each states only what its rule reads.</summary>
internal static class Entries
{
    public static readonly WorkingCopyInfo Info = new(
        Path.GetFullPath("/studio/game"),
        "https://svn.example/game",
        "uuid-1",
        31
    );

    public static WorkingCopyEntry Entry(
        string relPath,
        NodeStatus status = NodeStatus.Modified,
        PropertyStatus propertyStatus = PropertyStatus.Unmodified,
        bool isConflicted = false,
        bool hasLockToken = false,
        bool isCopied = false,
        FileFingerprint? onDisk = null,
        NodeKind kind = NodeKind.File
    ) =>
        new(
            relPath,
            kind,
            status,
            propertyStatus,
            Revision: 7,
            Changelist: null,
            IsConflicted: isConflicted,
            HasLockToken: hasLockToken,
            IsWriteLocked: false,
            IsCopied: isCopied,
            OnDisk: onDisk
        );

    public static StatusResponse Listing(params WorkingCopyEntry[] entries) =>
        new(Info, entries, ServedFromWarmIndex: true, 0.5, UnfinishedOperations: 0, []);

    /// <summary>A listing in which D27 paired these renames.</summary>
    public static StatusResponse Listing(
        IReadOnlyList<UnrecordedMove> moves,
        params WorkingCopyEntry[] entries
    ) => new(Info, entries, ServedFromWarmIndex: true, 0.5, UnfinishedOperations: 0, moves);

    /// <summary>The two entries D27 pairs for a rename: the missing old path and the unversioned new one.</summary>
    public static WorkingCopyEntry[] RenameHalves(string from, string to) =>
        [Entry(from, NodeStatus.Missing), Entry(to, NodeStatus.Unversioned)];
}
