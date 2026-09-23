using Subverted.Core;

namespace Subverted.App.Presentation;

/// <summary>
/// One line of the change list, already shaped for display. Immutable: a node that changes is a new
/// row, which is what lets <see cref="ChangeListSynchronizer"/> tell a change from a no-op.
/// </summary>
/// <param name="RelPath">The key — slash-separated and relative to the root, as the daemon sends it.</param>
/// <param name="Name">The last segment, which is what a person looks for first.</param>
/// <param name="Folder">Everything before it, or empty at the root.</param>
/// <param name="OnDisk">
/// Not shown. Here so a file saved again at the same status is a new row, which is the only way the
/// diff pane learns that what it is showing went out of date.
/// </param>
/// <param name="HasHistory">
/// Whether <c>svn log</c> has anything to say about it. Not for an unversioned node (E155010) nor a
/// plain add (E195002); a copy carries its source's. A plain add that has since gone missing reads
/// as missing and is offered anyway — the listing does not say how it was scheduled.
/// </param>
public sealed record ChangeRow(
    string RelPath,
    string Name,
    string Folder,
    ChangeBadge Badge,
    bool IsCopied,
    bool HasPropertyChange,
    bool IsLocked,
    FileFingerprint? OnDisk,
    bool HasHistory
)
{
    public static ChangeRow From(WorkingCopyEntry entry)
    {
        var separator = entry.RelPath.LastIndexOf('/');
        var name = entry.RelPath.Length == 0 ? "." : entry.RelPath[(separator + 1)..];
        var folder = separator < 0 ? string.Empty : entry.RelPath[..separator];

        return new ChangeRow(
            entry.RelPath,
            name,
            folder,
            ChangeBadges.For(entry),
            entry.IsCopied,
            // The badge already says "Properties" when nothing else changed; the flag is for the
            // node whose content changed as well, where the badge cannot say both.
            entry.PropertyStatus == PropertyStatus.Modified
                && entry.Status != NodeStatus.Unmodified,
            entry.HasLockToken,
            entry.OnDisk,
            entry.Status switch
            {
                NodeStatus.Unversioned or NodeStatus.Ignored => false,
                NodeStatus.Added => entry.IsCopied,
                _ => true,
            }
        );
    }
}
