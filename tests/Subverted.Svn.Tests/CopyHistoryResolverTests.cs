using Subverted.Core;
using static Subverted.Svn.Tests.WcDbRowFactory;

namespace Subverted.Svn.Tests;

/// <summary>
/// <c>svn status</c>'s fourth column. Every case is a node of the <c>subverted-copy</c> fixture,
/// named for what SVN 1.8.15 printed for it.
/// </summary>
public sealed class CopyHistoryResolverTests
{
    /// <summary><c>file-copy.txt</c> printed <c>A  +</c>; <c>plain.txt</c> printed <c>A</c>.</summary>
    [Test]
    [Arguments(true, true)]
    [Arguments(false, false)]
    public async Task An_add_carries_history_exactly_when_it_came_from_somewhere(
        bool hasCopySource,
        bool expected
    )
    {
        var row = Row(opDepth: 2, hasCopySource: hasCopySource);

        await Assert.That(CopyHistoryResolver.IsCopied(row, NodeStatus.Added)).IsEqualTo(expected);
    }

    /// <summary>
    /// <c>dircopy/c5.txt</c> <c>   +</c>, <c>dircopy/c1.txt</c> <c>M  +</c> and, after a merge,
    /// <c>d3/a.txt</c> <c>C  +</c>. The copy is a fact about the row, so what happened to the
    /// content since does not take it away.
    /// </summary>
    [Test]
    [Arguments(NodeStatus.Unmodified)]
    [Arguments(NodeStatus.Modified)]
    [Arguments(NodeStatus.Conflicted)]
    [Arguments(NodeStatus.Replaced)]
    public async Task A_copied_node_keeps_its_history_whatever_its_content_did(NodeStatus status)
    {
        await Assert.That(CopyHistoryResolver.IsCopied(Row(opDepth: 1), status)).IsTrue();
    }

    /// <summary>
    /// A BASE row has a repository path too — every committed node does — and must not read as a
    /// copy of itself.
    /// </summary>
    [Test]
    public async Task A_base_node_is_never_a_copy()
    {
        var row = Row(opDepth: 0, hasCopySource: true);

        await Assert.That(CopyHistoryResolver.IsCopied(row, NodeStatus.Modified)).IsFalse();
    }

    /// <summary>
    /// <c>dircopy/c3.txt</c> printed <c>!</c> and <c>dircopy/c5.txt</c> with a directory in its
    /// place printed <c>~</c>, where their untouched siblings printed <c>   +</c>. The same held
    /// for a missing copy root and a missing replace: what is on disk is not the copy.
    /// </summary>
    [Test]
    [Arguments(NodeStatus.Missing)]
    [Arguments(NodeStatus.Obstructed)]
    public async Task A_copy_not_on_disk_as_copied_shows_no_history(NodeStatus status)
    {
        await Assert.That(CopyHistoryResolver.IsCopied(Row(opDepth: 1), status)).IsFalse();
    }

    /// <summary>
    /// <c>dircopy/c2.txt</c>, deleted inside the copy, printed <c>D  +</c>; <c>moveme.txt</c>,
    /// deleted from BASE by a move, printed <c>D</c>. A delete row has no repository path of its
    /// own, so it is the layer it deletes that decides.
    /// </summary>
    [Test]
    [Arguments(1, true)]
    [Arguments(0, false)]
    public async Task A_delete_carries_history_when_what_it_deletes_is_a_copy(
        int lowerOpDepth,
        bool expected
    )
    {
        var row = Row(
            presence: "base-deleted",
            opDepth: 2,
            lowerOpDepth: lowerOpDepth,
            hasCopySource: false
        );

        await Assert
            .That(CopyHistoryResolver.IsCopied(row, NodeStatus.Deleted))
            .IsEqualTo(expected);
    }
}
