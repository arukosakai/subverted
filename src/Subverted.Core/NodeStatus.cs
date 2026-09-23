namespace Subverted.Core;

/// <summary>
/// Working-copy state of a single node. <see cref="NeedsPristineCompare"/> is deliberate:
/// the metadata fast path can prove "unmodified" but can never prove "modified", so callers
/// must fall back to a content compare rather than guess.
/// </summary>
public enum NodeStatus
{
    Unmodified,
    NeedsPristineCompare,
    Modified,
    Added,
    Deleted,
    Replaced,
    Missing,
    Unversioned,
    Ignored,
    Conflicted,
    Incomplete,

    /// <summary>
    /// Versioned as one kind and present on disk as the other — a file with a directory in its
    /// place, or the reverse. <c>svn status</c> prints <c>~</c>. Resolving it as though the on-disk
    /// kind were the right one reports a broken working copy as clean.
    /// </summary>
    Obstructed,

    /// <summary>
    /// The root of an <c>svn:externals</c> checkout: a different working copy that happens to sit
    /// inside this one. <c>svn status</c> prints <c>X</c> and does not fold its contents into the
    /// parent's listing. Not unversioned — telling someone to add it would be wrong.
    /// </summary>
    External,
}
