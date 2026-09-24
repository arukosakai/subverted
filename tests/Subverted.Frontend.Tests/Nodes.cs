using Subverted.Core;

namespace Subverted.Frontend.Tests;

/// <summary>Listing entries with boring defaults, so each test states only what its rule reads.</summary>
internal static class Nodes
{
    public static WorkingCopyEntry Node(
        string relPath,
        NodeStatus status = NodeStatus.Unmodified,
        PropertyStatus propertyStatus = PropertyStatus.Unmodified,
        bool isConflicted = false,
        bool isCopied = false,
        bool hasLockToken = false,
        NodeKind kind = NodeKind.File
    ) =>
        new(
            relPath,
            kind,
            status,
            propertyStatus,
            Revision: 3,
            Changelist: null,
            IsConflicted: isConflicted,
            HasLockToken: hasLockToken,
            IsWriteLocked: false,
            IsCopied: isCopied
        );
}
