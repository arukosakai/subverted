using Subverted.App.Presentation;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

public sealed class ChangeFoldersTests
{
    [Test]
    public async Task A_clean_copy_has_no_folders_at_all()
    {
        await Assert.That(ChangeFolders.Of([], "game")).IsEmpty();
    }

    [Test]
    public async Task Changes_at_the_root_give_only_the_root_counting_them_all()
    {
        var folders = ChangeFolders.Of([Row("a.txt"), Row("b.txt")], "game");

        await Assert.That(folders).IsEquivalentTo([new FolderLine("", "game", 0, 2)]);
    }

    [Test]
    public async Task Every_folder_above_a_change_is_listed_with_its_depth_and_last_segment()
    {
        var folders = ChangeFolders.Of([Row("art/chars/hero.png")], "game");

        await Assert
            .That(folders)
            .IsEquivalentTo([
                new FolderLine("", "game", 0, 1),
                new FolderLine("art", "art", 1, 1),
                new FolderLine("art/chars", "chars", 2, 1),
            ]);
    }

    /// <summary>A folder's count is everything below it, not just its own files.</summary>
    [Test]
    public async Task A_folder_counts_the_changes_at_every_depth_below_it()
    {
        var folders = ChangeFolders.Of(
            [Row("art/a.png"), Row("art/chars/hero.png"), Row("src/main.cs")],
            "game"
        );

        await Assert
            .That(folders.Select(folder => (folder.RelPath, folder.Count)))
            .IsEquivalentTo([("", 3), ("art", 2), ("art/chars", 1), ("src", 1)]);
    }

    [Test]
    public async Task A_folder_comes_straight_after_its_parent_and_siblings_sort_by_name_ignoring_case()
    {
        var folders = ChangeFolders.Of(
            [Row("src/x.cs"), Row("Art/b/y.png"), Row("art2/z.png"), Row("Art/a/w.png")],
            "game"
        );

        await Assert
            .That(string.Join(",", folders.Select(folder => folder.RelPath)))
            .IsEqualTo(",Art,Art/a,Art/b,art2,src");
    }

    /// <summary>Two folders differing only in case are both real on Linux, and each gets its own line.</summary>
    [Test]
    public async Task Folders_that_differ_only_in_case_are_kept_apart_in_a_fixed_order()
    {
        var folders = ChangeFolders.Of([Row("art/a.png"), Row("Art/b.png")], "game");

        await Assert
            .That(string.Join(",", folders.Select(folder => folder.RelPath)))
            .IsEqualTo(",Art,art");
    }

    /// <summary>A changed folder is a row in the table, not a branch of the tree.</summary>
    [Test]
    public async Task A_changed_folder_with_nothing_listed_under_it_is_not_a_tree_line()
    {
        var folders = ChangeFolders.Of([Row("assets", NodeStatus.Unversioned)], "game");

        await Assert.That(folders.Select(folder => folder.RelPath)).IsEquivalentTo([""]);
    }

    [Test]
    public async Task A_rename_puts_both_of_its_folders_in_the_tree_and_counts_in_each()
    {
        var rename = ChangeRow.Rename(
            Entry("new/hero.png", NodeStatus.Unversioned),
            "old/hero.png"
        );

        var folders = ChangeFolders.Of([rename], "game");

        await Assert
            .That(folders.Select(folder => (folder.RelPath, folder.Count)))
            .IsEquivalentTo([("", 1), ("new", 1), ("old", 1)]);
    }

    [Test]
    [Arguments("", true)]
    [Arguments("art", true)]
    [Arguments("art/chars", true)]
    [Arguments("art/chars/hero.png", true)]
    [Arguments("src", false)]
    [Arguments("ar", false)]
    [Arguments("art/char", false)]
    [Arguments("ART", false)]
    public async Task A_folder_keeps_the_rows_inside_it_and_only_those(string folder, bool kept)
    {
        await Assert
            .That(ChangeFolders.Contains(folder, Row("art/chars/hero.png")))
            .IsEqualTo(kept);
    }

    [Test]
    [Arguments("old", true)]
    [Arguments("new", true)]
    [Arguments("other", false)]
    public async Task A_rename_is_kept_by_the_folder_of_either_name(string folder, bool kept)
    {
        var rename = ChangeRow.Rename(
            Entry("new/hero.png", NodeStatus.Unversioned),
            "old/hero.png"
        );

        await Assert.That(ChangeFolders.Contains(folder, rename)).IsEqualTo(kept);
    }

    [Test]
    [Arguments("", true)]
    [Arguments("art", false)]
    public async Task Only_the_root_is_the_root(string relPath, bool isRoot)
    {
        await Assert.That(new FolderLine(relPath, relPath, 0, 1).IsRoot).IsEqualTo(isRoot);
    }

    [Test]
    [Arguments(1, "art, 1 change")]
    [Arguments(2, "art, 2 changes")]
    [Arguments(1500, "art, 1,500 changes")]
    public async Task A_screen_reader_hears_the_folder_and_its_count(int count, string said)
    {
        await Assert.That(new FolderLine("art", "art", 1, count).AutomationName).IsEqualTo(said);
    }

    private static ChangeRow Row(string relPath, NodeStatus status = NodeStatus.Modified) =>
        ChangeRow.From(Entry(relPath, status));
}
