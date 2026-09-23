namespace Subverted.Core;

/// <summary>
/// State of a node's versioned properties, independent of its content. This is SVN's second
/// status column: a node can be clean on one axis and dirty on the other.
/// </summary>
/// <remarks>
/// There is no <c>NeedsCompare</c> member here, unlike <see cref="NodeStatus"/> — both property
/// sets live in wc.db, so the comparison is exact and never escalates to the filesystem.
/// </remarks>
public enum PropertyStatus
{
    Unmodified,
    Modified,
}
