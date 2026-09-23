using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// Decides <c>svn status</c>'s fourth column, <c>+</c>: whether a node is scheduled with history.
/// Pure, like <see cref="NodeStatusResolver"/>, and taking that resolver's answer rather than
/// repeating it — the column depends on what the node turned out to be, not only on its row.
/// </summary>
internal static class CopyHistoryResolver
{
    /// <param name="status">What <see cref="NodeStatusResolver"/> made of the same row.</param>
    public static bool IsCopied(WcDbRow row, NodeStatus status) =>
        status switch
        {
            // svn drops the + here: what is on disk, if anything, is not the copy.
            NodeStatus.Missing or NodeStatus.Obstructed => false,

            // A delete row names nothing itself; the layer it deletes is BASE or a copy, because
            // deleting a plain add removes the add's row instead of layering over it.
            NodeStatus.Deleted => row.LowerOpDepth > 0,

            _ => row.OpDepth > 0 && row.HasCopySource,
        };
}
