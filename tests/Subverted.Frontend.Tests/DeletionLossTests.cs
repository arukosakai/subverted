using Subverted.Core;
using static Subverted.Frontend.Tests.Nodes;

namespace Subverted.Frontend.Tests;

/// <summary>What <c>svn delete --force</c> does to each node it reaches, as measured on 1.8.15.</summary>
public sealed class DeletionLossTests
{
    [Test]
    public async Task A_clean_file_is_deleted_and_revert_brings_it_back()
    {
        await Assert
            .That(DeletionLoss.For(Node("a.png")))
            .IsEqualTo(
                new DeletionLine(
                    "a.png",
                    "Deleted from disk; Revert brings it back until the delete is committed",
                    LosesWork: false
                )
            );
    }

    [Test]
    public async Task A_clean_node_inside_a_copy_goes_with_the_copy_and_its_source_keeps_it()
    {
        await Assert
            .That(DeletionLoss.For(Node("copy/a.png", isCopied: true)))
            .IsEqualTo(
                new DeletionLine(
                    "copy/a.png",
                    "Deleted with the copy; its source still has it",
                    LosesWork: false
                )
            );
    }

    [Test]
    public async Task A_missing_node_is_only_recorded_as_deleted()
    {
        await Assert
            .That(DeletionLoss.For(Node("gone.png", NodeStatus.Missing)))
            .IsEqualTo(
                new DeletionLine(
                    "gone.png",
                    "Already gone from disk; recorded as deleted",
                    LosesWork: false
                )
            );
    }

    [Test]
    public async Task A_node_already_scheduled_for_deletion_is_left_alone()
    {
        await Assert.That(DeletionLoss.For(Node("old.png", NodeStatus.Deleted))).IsNull();
    }

    [Test]
    public async Task An_edited_file_loses_its_edits()
    {
        await Assert
            .That(DeletionLoss.For(Node("a.png", NodeStatus.Modified)))
            .IsEqualTo(
                new DeletionLine("a.png", "Deleted from disk, and its edits with it", true)
            );
    }

    [Test]
    public async Task An_edited_file_with_property_changes_loses_both()
    {
        var node = Node("a.png", NodeStatus.Modified, PropertyStatus.Modified);

        await Assert
            .That(DeletionLoss.For(node))
            .IsEqualTo(
                new DeletionLine(
                    "a.png",
                    "Deleted from disk, and its edits and property changes with it",
                    true
                )
            );
    }

    /// <summary>A property-only change reads as unmodified on the content axis, and is still lost.</summary>
    [Test]
    public async Task A_property_change_alone_is_lost()
    {
        var node = Node("art", propertyStatus: PropertyStatus.Modified, kind: NodeKind.Directory);

        await Assert
            .That(DeletionLoss.For(node))
            .IsEqualTo(
                new DeletionLine("art", "Deleted from disk, and its property changes with it", true)
            );
    }

    [Test]
    public async Task A_property_change_inside_a_copy_is_reported_before_the_copy()
    {
        var node = Node("copy/a.png", propertyStatus: PropertyStatus.Modified, isCopied: true);

        await Assert.That(DeletionLoss.For(node)!.LosesWork).IsTrue();
    }

    /// <summary>Measured: an added file has no pristine, so the delete takes the only copy there is.</summary>
    [Test]
    public async Task A_plain_add_is_deleted_for_good()
    {
        await Assert
            .That(DeletionLoss.For(Node("new.png", NodeStatus.Added)))
            .IsEqualTo(
                new DeletionLine(
                    "new.png",
                    "Deleted from disk; it was never committed, so nothing can bring it back",
                    true
                )
            );
    }

    /// <summary>Measured: an edited copied file still reads <c>A  +</c>, so its edits cannot be ruled out.</summary>
    [Test]
    public async Task A_copy_is_undone_and_may_take_edits_with_it()
    {
        await Assert
            .That(DeletionLoss.For(Node("copy.png", NodeStatus.Added, isCopied: true)))
            .IsEqualTo(
                new DeletionLine(
                    "copy.png",
                    "The copy is undone and deleted from disk, with any edits made to it; its source is untouched",
                    true
                )
            );
    }

    [Test]
    public async Task A_replacement_is_deleted_for_good_and_the_original_marked_deleted()
    {
        await Assert
            .That(DeletionLoss.For(Node("a.png", NodeStatus.Replaced)))
            .IsEqualTo(
                new DeletionLine(
                    "a.png",
                    "The replacement is deleted from disk and nothing can bring it back; the original is marked deleted",
                    true
                )
            );
    }

    [Test]
    public async Task What_sits_in_an_obstructed_node_s_place_is_deleted()
    {
        await Assert
            .That(DeletionLoss.For(Node("a.png", NodeStatus.Obstructed)))
            .IsEqualTo(
                new DeletionLine("a.png", "Whatever is in its place on disk is deleted", true)
            );
    }

    [Test]
    public async Task An_unversioned_file_is_deleted_for_good()
    {
        await Assert
            .That(DeletionLoss.For(Node("notes.txt", NodeStatus.Unversioned)))
            .IsEqualTo(
                new DeletionLine(
                    "notes.txt",
                    "Not in SVN: deleted from disk, and nothing can bring it back",
                    true
                )
            );
    }

    [Test]
    public async Task An_ignored_file_is_deleted_for_good()
    {
        await Assert
            .That(DeletionLoss.For(Node("build.log", NodeStatus.Ignored)))
            .IsEqualTo(
                new DeletionLine(
                    "build.log",
                    "Ignored by SVN: deleted from disk, and nothing can bring it back",
                    true
                )
            );
    }

    /// <summary>Measured: the external's edits and unversioned files go too, and SVN names none of them.</summary>
    [Test]
    public async Task An_external_is_deleted_with_everything_in_it()
    {
        await Assert
            .That(DeletionLoss.For(Node("lib", NodeStatus.External, kind: NodeKind.Directory)))
            .IsEqualTo(
                new DeletionLine(
                    "lib",
                    "An external checkout: deleted from disk with everything in it, its own edits included, and this list cannot see inside it",
                    true
                )
            );
    }

    [Test]
    [Arguments(NodeStatus.Modified)]
    [Arguments(NodeStatus.Conflicted)]
    [Arguments(NodeStatus.Deleted)]
    public async Task A_conflict_is_reported_before_the_node_s_own_status(NodeStatus status)
    {
        var node = Node("clash.png", status, isConflicted: true);

        await Assert
            .That(DeletionLoss.For(node))
            .IsEqualTo(
                new DeletionLine(
                    "clash.png",
                    "Its conflict is dropped, and any local edits with it",
                    true
                )
            );
    }

    [Test]
    public async Task A_conflicted_status_without_the_flag_is_still_a_conflict()
    {
        await Assert
            .That(DeletionLoss.For(Node("clash.png", NodeStatus.Conflicted))!.What)
            .IsEqualTo("Its conflict is dropped, and any local edits with it");
    }

    [Test]
    [Arguments(NodeStatus.NeedsPristineCompare)]
    [Arguments(NodeStatus.Incomplete)]
    public async Task A_node_whose_edits_are_not_known_is_taken_to_have_some(NodeStatus status)
    {
        await Assert
            .That(DeletionLoss.For(Node("a.png", status)))
            .IsEqualTo(new DeletionLine("a.png", "Deleted from disk, with any edits it has", true));
    }
}
