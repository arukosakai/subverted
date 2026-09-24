using Subverted.Core;
using static Subverted.Frontend.Tests.Nodes;

namespace Subverted.Frontend.Tests;

/// <summary>Which nodes are offered a delete, from what <c>svn delete --force</c> did to each on 1.8.15.</summary>
public sealed class DeletionOfferTests
{
    private const StringComparison Ordinal = StringComparison.Ordinal;

    [Test]
    [Arguments(NodeStatus.Unmodified)]
    [Arguments(NodeStatus.Modified)]
    [Arguments(NodeStatus.Added)]
    [Arguments(NodeStatus.Missing)]
    [Arguments(NodeStatus.Replaced)]
    [Arguments(NodeStatus.NeedsPristineCompare)]
    public async Task A_versioned_node_is_offered_a_delete(NodeStatus status)
    {
        var target = Node("a.png", status);

        await Assert.That(DeletionOffer.RefusalFor(target, [target], Ordinal)).IsNull();
    }

    [Test]
    public async Task A_copy_and_a_locked_file_are_offered_a_delete()
    {
        var copy = Node("copy.png", NodeStatus.Added, isCopied: true);
        var held = Node("held.png", hasLockToken: true);

        await Assert.That(DeletionOffer.RefusalFor(copy, [copy], Ordinal)).IsNull();
        await Assert.That(DeletionOffer.RefusalFor(held, [held], Ordinal)).IsNull();
    }

    [Test]
    [Arguments(NodeStatus.Unversioned)]
    [Arguments(NodeStatus.Ignored)]
    public async Task A_node_svn_does_not_track_is_not_its_to_delete(NodeStatus status)
    {
        var target = Node("notes.txt", status);

        await Assert
            .That(DeletionOffer.RefusalFor(target, [target], Ordinal))
            .IsEqualTo(
                "notes.txt is not in SVN, so there is nothing for SVN to delete. A file manager can remove it."
            );
    }

    [Test]
    public async Task An_external_is_removed_through_its_property_not_deleted()
    {
        var target = Node("lib", NodeStatus.External, kind: NodeKind.Directory);

        await Assert
            .That(DeletionOffer.RefusalFor(target, [target], Ordinal))
            .IsEqualTo(
                "lib is an external checkout. It goes by changing svn:externals on the folder that holds it."
            );
    }

    /// <summary>Measured: SVN refuses it with <c>E155035</c>, so offering it could only ever fail.</summary>
    [Test]
    public async Task The_working_copy_root_is_not_offered()
    {
        var root = Node("", propertyStatus: PropertyStatus.Modified, kind: NodeKind.Directory);

        await Assert
            .That(DeletionOffer.RefusalFor(root, [root], Ordinal))
            .IsEqualTo("SVN cannot delete the root of a working copy.");
    }

    [Test]
    public async Task A_node_already_marked_deleted_is_not_offered_again()
    {
        var target = Node("old.png", NodeStatus.Deleted);

        await Assert
            .That(DeletionOffer.RefusalFor(target, [target], Ordinal))
            .IsEqualTo("old.png is already marked deleted.");
    }

    /// <summary>
    /// Measured: <c>svn delete --force</c> on a file with a folder in its place fails in its work queue,
    /// and <c>svn cleanup</c> then fails the same way until someone moves the folder by hand.
    /// </summary>
    [Test]
    public async Task An_obstructed_node_is_not_offered_because_svn_would_wedge_the_working_copy()
    {
        var target = Node("a.png", NodeStatus.Obstructed);

        await Assert
            .That(DeletionOffer.RefusalFor(target, [target], Ordinal))
            .IsEqualTo(
                "Something else is in a.png's place on disk. Deleting it makes SVN stop part-way and "
                    + "leaves a cleanup that fails too; move what is there out of the way first."
            );
    }

    /// <summary>Measured: the delete also removes the conflict's own files beside it, which no line names.</summary>
    [Test]
    [Arguments(NodeStatus.Modified, true)]
    [Arguments(NodeStatus.Conflicted, false)]
    [Arguments(NodeStatus.Deleted, true)]
    public async Task A_conflicted_node_is_resolved_first_whatever_else_it_is(
        NodeStatus status,
        bool isConflicted
    )
    {
        var target = Node("clash.png", status, isConflicted: isConflicted);

        await Assert
            .That(DeletionOffer.RefusalFor(target, [target], Ordinal))
            .IsEqualTo(
                "clash.png is in conflict. Resolve it first: deleting it would also remove the "
                    + "conflict's own files beside it, which this list cannot name."
            );
    }

    [Test]
    public async Task An_incomplete_node_is_updated_first()
    {
        var target = Node("art", NodeStatus.Incomplete, kind: NodeKind.Directory);

        await Assert
            .That(DeletionOffer.RefusalFor(target, [target], Ordinal))
            .IsEqualTo("art was left part-way through an update. Update it before deleting it.");
    }

    [Test]
    public async Task A_folder_holding_an_incomplete_node_is_updated_first()
    {
        var target = Node("art", kind: NodeKind.Directory);
        WorkingCopyEntry[] listing =
        [
            target,
            Node("art/a.png", NodeStatus.Modified),
            Node("art/sub", NodeStatus.Incomplete, kind: NodeKind.Directory),
        ];

        await Assert
            .That(DeletionOffer.RefusalFor(target, listing, Ordinal))
            .IsEqualTo(
                "art/sub was left part-way through an update. Update it before deleting art."
            );
    }

    [Test]
    public async Task An_incomplete_node_beside_the_folder_does_not_stop_it()
    {
        var target = Node("art", kind: NodeKind.Directory);
        WorkingCopyEntry[] listing =
        [
            target,
            Node("artefacts", NodeStatus.Incomplete, kind: NodeKind.Directory),
            Node("docs/sub", NodeStatus.Incomplete, kind: NodeKind.Directory),
        ];

        await Assert.That(DeletionOffer.RefusalFor(target, listing, Ordinal)).IsNull();
    }

    /// <summary>The platform's comparison decides what is beneath the target, so both answers are held.</summary>
    [Test]
    [Arguments(StringComparison.Ordinal, false)]
    [Arguments(StringComparison.OrdinalIgnoreCase, true)]
    public async Task What_lies_beneath_is_decided_by_the_platform_s_comparison(
        StringComparison comparison,
        bool refused
    )
    {
        var target = Node("art", kind: NodeKind.Directory);
        WorkingCopyEntry[] listing = [target, Node("Art/sub", NodeStatus.Incomplete)];

        await Assert
            .That(DeletionOffer.RefusalFor(target, listing, comparison) is not null)
            .IsEqualTo(refused);
    }

    [Test]
    public async Task A_conflict_or_obstruction_beneath_a_folder_does_not_stop_it()
    {
        var target = Node("art", kind: NodeKind.Directory);
        WorkingCopyEntry[] listing =
        [
            target,
            Node("art/clash.png", NodeStatus.Conflicted, isConflicted: true),
            Node("art/obs.png", NodeStatus.Obstructed),
            Node("art/notes.txt", NodeStatus.Unversioned),
            Node("art/lib", NodeStatus.External, kind: NodeKind.Directory),
        ];

        await Assert.That(DeletionOffer.RefusalFor(target, listing, Ordinal)).IsNull();
    }
}
