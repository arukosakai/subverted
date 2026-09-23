using Subverted.Core;

namespace Subverted.App.Presentation;

/// <summary>
/// Which badge a node gets. Pure, so every rule is a test; the precedence is the part that matters —
/// a conflict outranks whatever the content axis says, and a property-only change is still a change.
/// </summary>
public static class ChangeBadges
{
    public static ChangeBadge For(WorkingCopyEntry entry)
    {
        if (entry.IsConflicted || entry.Status == NodeStatus.Conflicted)
        {
            return new ChangeBadge("Conflict", ChangeTone.Conflict);
        }

        return entry.Status switch
        {
            NodeStatus.Modified => new ChangeBadge("Modified", ChangeTone.Modified),
            NodeStatus.Added => new ChangeBadge(
                entry.IsCopied ? "Copied" : "Added",
                ChangeTone.Added
            ),
            NodeStatus.Deleted => new ChangeBadge("Deleted", ChangeTone.Deleted),
            NodeStatus.Replaced => new ChangeBadge("Replaced", ChangeTone.Replaced),
            NodeStatus.Missing => new ChangeBadge("Missing", ChangeTone.Missing),
            NodeStatus.Incomplete => new ChangeBadge("Incomplete", ChangeTone.Missing),
            NodeStatus.Obstructed => new ChangeBadge("Obstructed", ChangeTone.Missing),
            NodeStatus.Unversioned => new ChangeBadge("Not versioned", ChangeTone.Unversioned),
            NodeStatus.Ignored => new ChangeBadge("Ignored", ChangeTone.Quiet),
            NodeStatus.External => new ChangeBadge("External", ChangeTone.Quiet),
            NodeStatus.NeedsPristineCompare => new ChangeBadge("Undecided", ChangeTone.Quiet),
            _ => Unchanged(entry),
        };
    }

    /// <summary>
    /// A node that is listed while its content is clean: its properties changed, or it carries
    /// a lock of some kind. Only the first of those is something a commit sends.
    /// </summary>
    private static ChangeBadge Unchanged(WorkingCopyEntry entry) =>
        entry.PropertyStatus == PropertyStatus.Modified
            ? new ChangeBadge("Properties", ChangeTone.Modified)
            : new ChangeBadge("Unchanged", ChangeTone.Quiet);
}
