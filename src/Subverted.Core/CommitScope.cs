namespace Subverted.Core;

/// <summary>
/// How far a commit reaches from the paths it was given. The two are different operations, not a
/// setting: one sends a subtree, the other sends a list somebody read and approved node by node.
/// </summary>
public enum CommitScope
{
    /// <summary>
    /// Everything changed under each path, which is what <c>svn commit</c> does by default and what
    /// <c>sv commit PATH...</c> means.
    /// </summary>
    WholeSubtree,

    /// <summary>
    /// The named nodes and nothing below them. A directory sends its own property change while its
    /// children stay local — without which a picked set would send changes that were just declined.
    /// </summary>
    ExactlyTheseNodes,
}
