using Subverted.App.Presentation;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>What a commit of the ticks sends: shown ticks only, both halves of a rename, and D20.</summary>
public sealed class TickedSelectionTests
{
    [Test]
    public async Task Ticked_rows_are_sent_in_path_order_and_unticked_ones_are_not()
    {
        var rows = Rows(Entry("b.png"), Entry("a.png"), Entry("c.png"));

        var selection = TickedSelection.Of(rows, Set("b.png", "a.png"), AllOf(rows));

        await Assert.That(Paths(selection)).IsEqualTo("a.png,b.png");
        await Assert.That(selection.Sent.Count).IsEqualTo(2);
        await Assert.That(selection.DecidedByFolder).IsEmpty();
    }

    [Test]
    public async Task Nothing_ticked_sends_nothing()
    {
        var rows = Rows(Entry("a.png"));

        var selection = TickedSelection.Of(rows, Set(), AllOf(rows));

        await Assert.That(selection.Sent).IsEmpty();
        await Assert.That(selection.RelPaths).IsEmpty();
        await Assert.That(TickedSelection.Nothing.Sent).IsEmpty();
    }

    /// <summary>The operator's call: a commit never includes a change the person cannot see.</summary>
    [Test]
    public async Task A_tick_the_filter_hides_is_not_sent()
    {
        var rows = Rows(Entry("art/a.png"), Entry("src/b.cs"));

        var selection = TickedSelection.Of(rows, Set("art/a.png", "src/b.cs"), Set("art/a.png"));

        await Assert.That(Paths(selection)).IsEqualTo("art/a.png");
    }

    [Test]
    public async Task The_same_tick_is_sent_again_once_the_filter_shows_it()
    {
        var rows = Rows(Entry("art/a.png"), Entry("src/b.cs"));
        var ticked = Set("art/a.png", "src/b.cs");

        var selection = TickedSelection.Of(rows, ticked, AllOf(rows));

        await Assert.That(Paths(selection)).IsEqualTo("art/a.png,src/b.cs");
    }

    /// <summary>The daemon refuses half a pair, so the row names both, old path first.</summary>
    [Test]
    public async Task A_rename_row_sends_both_of_its_paths()
    {
        List<ChangeRow> rows =
        [
            ChangeRow.Rename(Entry("art/protagonist.png", NodeStatus.Unversioned), "art/hero.png"),
            ChangeRow.From(Entry("readme.txt")),
        ];

        var selection = TickedSelection.Of(
            rows,
            Set("art/protagonist.png", "readme.txt"),
            AllOf(rows)
        );

        await Assert
            .That(string.Join(",", selection.RelPaths))
            .IsEqualTo("art/hero.png,art/protagonist.png,readme.txt");
        await Assert.That(selection.Sent.Count).IsEqualTo(2);
    }

    /// <summary>E200009: a child whose added parent is not in the same commit is refused outright.</summary>
    [Test]
    public async Task Beneath_an_added_folder_left_out_nothing_is_a_choice_or_sent()
    {
        var rows = Rows(
            Entry("new/child.png", NodeStatus.Added),
            Entry("new", NodeStatus.Added, kind: NodeKind.Directory)
        );

        var selection = TickedSelection.Of(rows, Set("new/child.png"), AllOf(rows));

        await Assert.That(selection.Sent).IsEmpty();
        await Assert.That(selection.DecidedByFolder).IsEquivalentTo(new[] { "new/child.png" });
    }

    /// <summary>Named together they go together; the sibling nobody ticked stays local.</summary>
    [Test]
    public async Task Beneath_an_added_folder_that_is_sent_each_child_is_its_own_choice()
    {
        var rows = Rows(
            Entry("new", NodeStatus.Added, kind: NodeKind.Directory),
            Entry("new/picked.png", NodeStatus.Added),
            Entry("new/left.png", NodeStatus.Added)
        );

        var selection = TickedSelection.Of(rows, Set("new", "new/picked.png"), AllOf(rows));

        await Assert.That(Paths(selection)).IsEqualTo("new,new/picked.png");
        await Assert.That(selection.DecidedByFolder).IsEmpty();
    }

    /// <summary>A deletion is recorded on the folder, so its children go with it whichever way.</summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Deletions_beneath_a_deleted_folder_follow_it_and_are_not_named(
        bool folderTicked
    )
    {
        var rows = Rows(
            Entry("old", NodeStatus.Deleted, kind: NodeKind.Directory),
            Entry("old/a.png", NodeStatus.Deleted)
        );
        var ticked = folderTicked ? Set("old", "old/a.png") : Set("old/a.png");

        var selection = TickedSelection.Of(rows, ticked, AllOf(rows));

        await Assert.That(Paths(selection)).IsEqualTo(folderTicked ? "old" : "");
        await Assert.That(selection.DecidedByFolder).IsEquivalentTo(new[] { "old/a.png" });
    }

    /// <summary>A replaced folder is both rules at once: deletions carried, new children still to decide.</summary>
    [Test]
    public async Task Beneath_a_replaced_folder_that_is_sent_deletions_follow_and_additions_are_a_choice()
    {
        var rows = Rows(
            Entry("dir", NodeStatus.Replaced, kind: NodeKind.Directory),
            Entry("dir/gone.png", NodeStatus.Deleted),
            Entry("dir/new.png", NodeStatus.Added)
        );

        var selection = TickedSelection.Of(
            rows,
            Set("dir", "dir/gone.png", "dir/new.png"),
            AllOf(rows)
        );

        await Assert.That(Paths(selection)).IsEqualTo("dir,dir/new.png");
        await Assert.That(selection.DecidedByFolder).IsEquivalentTo(new[] { "dir/gone.png" });
    }

    [Test]
    public async Task Beneath_a_replaced_folder_left_out_nothing_is_a_choice()
    {
        var rows = Rows(
            Entry("dir", NodeStatus.Replaced, kind: NodeKind.Directory),
            Entry("dir/gone.png", NodeStatus.Deleted),
            Entry("dir/new.png", NodeStatus.Added)
        );

        var selection = TickedSelection.Of(rows, Set("dir/new.png"), AllOf(rows));

        await Assert.That(selection.Sent).IsEmpty();
        await Assert
            .That(selection.DecidedByFolder)
            .IsEquivalentTo(new[] { "dir/gone.png", "dir/new.png" });
    }

    /// <summary>
    /// Measured on 1.8.15 (forum #40): `svn delete` of a missing folder records its whole missing
    /// subtree, so an untick beneath a ticked one would be a lie.
    /// </summary>
    [Test]
    public async Task Beneath_a_missing_folder_that_is_sent_nothing_is_a_choice()
    {
        var rows = Rows(
            Entry("gone", NodeStatus.Missing, kind: NodeKind.Directory),
            Entry("gone/a.txt", NodeStatus.Missing),
            Entry("gone/deep", NodeStatus.Missing, kind: NodeKind.Directory),
            Entry("gone/deep/b.txt", NodeStatus.Missing),
            Entry("gone-too.txt", NodeStatus.Missing)
        );

        var selection = TickedSelection.Of(rows, Set("gone", "gone-too.txt"), AllOf(rows));

        await Assert.That(Paths(selection)).IsEqualTo("gone,gone-too.txt");
        await Assert
            .That(selection.DecidedByFolder)
            .IsEquivalentTo(new[] { "gone/a.txt", "gone/deep", "gone/deep/b.txt" });
    }

    /// <summary>Left alone, a missing folder settles nothing: each child can be deleted on its own.</summary>
    [Test]
    public async Task Beneath_a_missing_folder_left_out_each_missing_child_is_its_own_choice()
    {
        var rows = Rows(
            Entry("gone", NodeStatus.Missing, kind: NodeKind.Directory),
            Entry("gone/a.txt", NodeStatus.Missing),
            Entry("gone/b.txt", NodeStatus.Missing)
        );

        var selection = TickedSelection.Of(rows, Set("gone/a.txt"), AllOf(rows));

        await Assert.That(Paths(selection)).IsEqualTo("gone/a.txt");
        await Assert.That(selection.DecidedByFolder).IsEmpty();
    }

    /// <summary>Both halves of the rule: a missing file, or a folder that is only edited, carries nothing.</summary>
    [Test]
    public async Task Only_a_missing_folder_carries_what_is_beneath_it()
    {
        var rows = Rows(
            Entry("gone", NodeStatus.Missing),
            Entry("gone/x", NodeStatus.Missing),
            Entry("props", NodeStatus.Modified, kind: NodeKind.Directory),
            Entry("props/y", NodeStatus.Missing)
        );

        var selection = TickedSelection.Of(rows, Set("gone", "props"), AllOf(rows));

        await Assert.That(selection.DecidedByFolder).IsEmpty();
    }

    /// <summary>A hidden folder is not sent, so it must not pull in what it would have carried.</summary>
    [Test]
    public async Task A_folder_the_filter_hides_decides_its_children_as_if_left_out()
    {
        var rows = Rows(
            Entry("new", NodeStatus.Added, kind: NodeKind.Directory),
            Entry("new/shown.png", NodeStatus.Added)
        );

        var selection = TickedSelection.Of(rows, Set("new", "new/shown.png"), Set("new/shown.png"));

        await Assert.That(selection.Sent).IsEmpty();
        await Assert.That(selection.DecidedByFolder).IsEquivalentTo(new[] { "new/shown.png" });
    }

    /// <summary>`art-old` sorts between `art` and `art/…` ordinally, and must not read as beneath it.</summary>
    [Test]
    public async Task A_sibling_sharing_a_folder_s_prefix_is_not_beneath_it()
    {
        var rows = Rows(
            Entry("art", NodeStatus.Added, kind: NodeKind.Directory),
            Entry("art-old.png", NodeStatus.Modified)
        );

        var selection = TickedSelection.Of(rows, Set("art-old.png"), AllOf(rows));

        await Assert.That(Paths(selection)).IsEqualTo("art-old.png");
    }

    /// <summary>SVN refuses a commit that names a conflict, so a ticked one waits for its resolve.</summary>
    [Test]
    public async Task A_ticked_conflict_is_not_sent_but_the_rest_of_the_ticks_are()
    {
        var rows = Rows(Entry("a.png", NodeStatus.Conflicted, isConflicted: true), Entry("b.png"));

        var selection = TickedSelection.Of(rows, Set("a.png", "b.png"), AllOf(rows));

        await Assert.That(Paths(selection)).IsEqualTo("b.png");
    }

    private static List<ChangeRow> Rows(params WorkingCopyEntry[] entries) =>
        [.. entries.Select(ChangeRow.From)];

    private static HashSet<string> Set(params string[] paths) => new(paths, StringComparer.Ordinal);

    private static HashSet<string> AllOf(IEnumerable<ChangeRow> rows) =>
        new(rows.Select(row => row.RelPath), StringComparer.Ordinal);

    private static string Paths(TickedSelection selection) =>
        string.Join(",", selection.Sent.Select(row => row.RelPath));
}
