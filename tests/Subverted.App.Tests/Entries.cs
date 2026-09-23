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
        bool isCopied = false
    ) =>
        new(
            relPath,
            NodeKind.File,
            status,
            propertyStatus,
            Revision: 7,
            Changelist: null,
            IsConflicted: isConflicted,
            HasLockToken: hasLockToken,
            IsWriteLocked: false,
            IsCopied: isCopied
        );

    public static StatusResponse Listing(params WorkingCopyEntry[] entries) =>
        new(Info, entries, ServedFromWarmIndex: true, 0.5, UnfinishedOperations: 0, []);
}
