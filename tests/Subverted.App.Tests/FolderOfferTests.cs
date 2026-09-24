using Subverted.App.Presentation;
using Subverted.Core;
using Subverted.Protocol;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>
/// The directory pane offers Revert… and Delete… by the line menu's rules, on folders that may have
/// no line of their own.
/// </summary>
public sealed class FolderOfferTests
{
    [Test]
    public async Task The_root_is_never_offered_for_deletion()
    {
        var rows = Rows(Entry("art/a.png"));

        await Assert.That(FolderOffer.CanDelete("", rows)).IsFalse();
    }

    [Test]
    public async Task The_root_is_not_offered_for_deletion_even_through_a_line_of_its_own()
    {
        var rows = Rows(
            Entry("", NodeStatus.Unmodified, PropertyStatus.Modified, kind: NodeKind.Directory),
            Entry("art/a.png")
        );

        await Assert.That(FolderOffer.CanDelete("", rows)).IsFalse();
    }

    [Test]
    public async Task A_folder_with_no_line_of_its_own_is_offered_for_deletion()
    {
        var rows = Rows(Entry("art/a.png"));

        await Assert.That(FolderOffer.CanDelete("art", rows)).IsTrue();
    }

    [Test]
    public async Task A_clean_folder_listed_with_every_file_is_offered_for_deletion()
    {
        var rows = Rows(
            Entry("art", NodeStatus.Unmodified, kind: NodeKind.Directory),
            Entry("art/a.png", NodeStatus.Unmodified)
        );

        await Assert.That(FolderOffer.CanDelete("art", rows)).IsTrue();
    }

    [Test]
    [Arguments(NodeStatus.Missing, true)]
    [Arguments(NodeStatus.Added, true)]
    [Arguments(NodeStatus.Modified, true)]
    [Arguments(NodeStatus.Replaced, true)]
    [Arguments(NodeStatus.Deleted, false)]
    [Arguments(NodeStatus.Obstructed, false)]
    [Arguments(NodeStatus.External, false)]
    [Arguments(NodeStatus.Incomplete, false)]
    public async Task A_folder_with_a_line_of_its_own_is_offered_as_that_line_would_be(
        NodeStatus status,
        bool expected
    )
    {
        var rows = Rows(
            Entry("art", status, kind: NodeKind.Directory),
            Entry("art/a.png", NodeStatus.Missing)
        );

        await Assert.That(FolderOffer.CanDelete("art", rows)).IsEqualTo(expected);
    }

    [Test]
    public async Task A_tree_conflicted_folder_is_not_offered_for_deletion()
    {
        var rows = Rows(
            Entry("art", NodeStatus.Modified, isConflicted: true, kind: NodeKind.Directory),
            Entry("art/a.png")
        );

        await Assert.That(FolderOffer.CanDelete("art", rows)).IsFalse();
    }

    [Test]
    public async Task A_folder_holding_a_half_updated_node_is_not_offered_but_its_sibling_is()
    {
        var rows = Rows(
            Entry("art/sub", NodeStatus.Incomplete, kind: NodeKind.Directory),
            Entry("art/sub/a.png"),
            Entry("src/b.cs")
        );

        await Assert.That(FolderOffer.CanDelete("art", rows)).IsFalse();
        await Assert.That(FolderOffer.CanDelete("src", rows)).IsTrue();
    }

    [Test]
    public async Task A_folder_holding_an_edit_is_offered_for_revert()
    {
        var rows = Rows(Entry("art/a.png"), Entry("src/b.cs", NodeStatus.Unversioned));

        await Assert.That(FolderOffer.CanRevert("art", "", rows)).IsTrue();
    }

    [Test]
    public async Task A_folder_holding_only_what_revert_leaves_alone_is_not_offered_for_revert()
    {
        var rows = Rows(Entry("art/a.png"), Entry("src/b.cs", NodeStatus.Unversioned));

        await Assert.That(FolderOffer.CanRevert("src", "", rows)).IsFalse();
    }

    [Test]
    public async Task A_clean_folder_is_not_offered_for_revert()
    {
        var rows = Rows(Entry("art/a.png"), Entry("src/b.cs", NodeStatus.Unmodified));

        await Assert.That(FolderOffer.CanRevert("src", "", rows)).IsFalse();
    }

    [Test]
    public async Task The_root_of_a_whole_listing_is_offered_for_revert_when_anything_under_it_would_change()
    {
        await Assert.That(FolderOffer.CanRevert("", "", Rows(Entry("art/a.png")))).IsTrue();
        await Assert
            .That(FolderOffer.CanRevert("", "", Rows(Entry("art/a.png", NodeStatus.Unversioned))))
            .IsFalse();
    }

    /// <summary>A listing scoped to a subfolder cannot name what a revert above it would reach.</summary>
    [Test]
    [Arguments("", false)]
    [Arguments("art", false)]
    [Arguments("art/chars", true)]
    [Arguments("art/chars/hero", true)]
    public async Task Revert_is_offered_only_at_or_beneath_the_folder_the_listing_was_scoped_to(
        string folder,
        bool expected
    )
    {
        var rows = Rows(Entry("art/chars/hero/a.png"));

        await Assert.That(FolderOffer.CanRevert(folder, "art/chars", rows)).IsEqualTo(expected);
    }

    /// <summary>Reverting the folder the missing half was in brings it back; the renamed file is left.</summary>
    [Test]
    public async Task A_rename_is_reverted_from_the_folder_its_old_name_was_in()
    {
        var rows = ChangeRows.From(
            RenameHalves("art/old.png", "src/new.png"),
            [new UnrecordedMove("art/old.png", "src/new.png")]
        );

        await Assert.That(FolderOffer.CanRevert("art", "", rows)).IsTrue();
        await Assert.That(FolderOffer.CanRevert("src", "", rows)).IsFalse();
    }

    private static IReadOnlyList<ChangeRow> Rows(params WorkingCopyEntry[] entries) =>
        [.. entries.Select(ChangeRow.From)];
}
