using Subverted.Core;

namespace Subverted.App.Presentation;

/// <summary>
/// Whether a change starts ticked when it first appears. Edits, missing files and renames do,
/// because they are the work. An unversioned file does not, because it is usually build output
/// and pre-ticking it is how junk reaches the repository.
/// </summary>
public static class DefaultTick
{
    /// <returns>
    /// False as well for anything the commit would refuse or has nothing to send for: a conflict,
    /// an obstruction, an ignored or external node, one still undecided, or a clean node listed
    /// only for its lock.
    /// </returns>
    public static bool For(ChangeRow row)
    {
        if (row.RenamedFrom is not null)
        {
            return true;
        }

        var entry = row.Entry;
        if (entry.IsConflicted)
        {
            return false;
        }

        return entry.Status switch
        {
            NodeStatus.Modified
            or NodeStatus.Added
            or NodeStatus.Deleted
            or NodeStatus.Replaced
            or NodeStatus.Missing => true,
            NodeStatus.Unmodified => entry.PropertyStatus == PropertyStatus.Modified,
            _ => false,
        };
    }
}
