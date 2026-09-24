using Subverted.App.ViewModels;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>The directory pane beside the table: choosing a folder narrows what is shown and sent.</summary>
public sealed class FolderPaneTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Test]
    public async Task The_pane_lists_the_changed_folders_and_starts_on_the_root()
    {
        var view = await ListedAsync(Entry("art/a.png"), Entry("src/b.cs"));

        await Assert.That(Folders(view)).IsEqualTo(":2,art:1,src:1");
        await Assert.That(view.SelectedFolder).IsSameReferenceAs(view.Folders[0]);
        await Assert.That(Keys(view)).IsEqualTo("art/a.png,src/b.cs");
    }

    [Test]
    public async Task A_clean_copy_has_an_empty_pane_and_nothing_chosen()
    {
        var view = await ListedAsync();

        await Assert.That(view.Folders).IsEmpty();
        await Assert.That(view.SelectedFolder).IsNull();
    }

    [Test]
    public async Task Choosing_a_folder_shows_only_what_is_inside_it_and_counts_the_rest()
    {
        var view = await ListedAsync(Entry("art/a.png"), Entry("src/b.cs"), Entry("src/c.cs"));

        view.SelectedFolder = view.Folders.Single(folder => folder.Content.RelPath == "art");

        await Assert.That(Keys(view)).IsEqualTo("art/a.png");
        await Assert.That(view.HiddenText).IsEqualTo("2 changes hidden");
    }

    /// <summary>Operator's rule for the filter holds for the folder too: what is out of sight is not sent.</summary>
    [Test]
    public async Task A_tick_outside_the_chosen_folder_is_kept_but_not_sent()
    {
        var view = await ListedAsync(Entry("art/a.png"), Entry("src/b.cs"));

        view.SelectedFolder = view.Folders.Single(folder => folder.Content.RelPath == "art");

        await Assert.That(view.Ticked).Contains("src/b.cs");
        await Assert
            .That(view.Composer.Selection.Sent.Select(row => row.RelPath))
            .IsEquivalentTo(["art/a.png"]);
    }

    [Test]
    public async Task Going_back_to_the_root_sends_every_tick_again()
    {
        var view = await ListedAsync(Entry("art/a.png"), Entry("src/b.cs"));
        view.SelectedFolder = view.Folders.Single(folder => folder.Content.RelPath == "art");

        view.SelectedFolder = view.Folders[0];

        await Assert
            .That(view.Composer.Selection.Sent.Select(row => row.RelPath))
            .IsEquivalentTo(["art/a.png", "src/b.cs"]);
        await Assert.That(view.HiddenText).IsNull();
    }

    [Test]
    public async Task The_folder_and_the_filter_narrow_together()
    {
        var view = await ListedAsync(Entry("art/a.png"), Entry("art/b.png"), Entry("src/a.cs"));
        view.SelectedFolder = view.Folders.Single(folder => folder.Content.RelPath == "art");

        view.Filter = "a.";

        await Assert.That(Keys(view)).IsEqualTo("art/a.png");
        await Assert.That(view.HiddenText).IsEqualTo("2 changes hidden");
    }

    [Test]
    public async Task Show_all_clears_the_filter_and_goes_back_to_the_root()
    {
        var view = await ListedAsync(Entry("art/a.png"), Entry("src/b.cs"));
        view.SelectedFolder = view.Folders.Single(folder => folder.Content.RelPath == "art");
        view.Filter = "a";

        view.ClearFilterCommand.Execute(null);

        await Assert.That(view.SelectedFolder).IsSameReferenceAs(view.Folders[0]);
        await Assert.That(view.Filter).IsEqualTo("");
        await Assert.That(Keys(view)).IsEqualTo("art/a.png,src/b.cs");
    }

    [Test]
    public async Task The_chosen_folder_and_its_line_survive_a_resync_that_changes_its_count()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("art/a.png"), Entry("src/b.cs")))
            .Answers(Listing(Entry("art/a.png"), Entry("art/c.png"), Entry("src/b.cs")));
        var view = View(status);
        await view.RefreshAsync(None);
        var art = view.Folders.Single(folder => folder.Content.RelPath == "art");
        view.SelectedFolder = art;

        await view.RefreshAsync(None);

        await Assert.That(view.SelectedFolder).IsSameReferenceAs(art);
        await Assert.That(art.Content.Count).IsEqualTo(2);
        await Assert.That(Keys(view)).IsEqualTo("art/a.png,art/c.png");
    }

    [Test]
    public async Task A_chosen_folder_that_empties_falls_back_to_the_root_instead_of_showing_nothing()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("art/a.png"), Entry("src/b.cs")))
            .Answers(Listing(Entry("src/b.cs")));
        var view = View(status);
        await view.RefreshAsync(None);
        view.SelectedFolder = view.Folders.Single(folder => folder.Content.RelPath == "art");

        await view.RefreshAsync(None);

        await Assert.That(view.SelectedFolder).IsSameReferenceAs(view.Folders[0]);
        await Assert.That(Folders(view)).IsEqualTo(":1,src:1");
        await Assert.That(Keys(view)).IsEqualTo("src/b.cs");
    }

    /// <summary>The list control writes null when it loses its item; that is not a person choosing nothing.</summary>
    [Test]
    public async Task Nothing_chosen_shows_everything()
    {
        var view = await ListedAsync(Entry("art/a.png"), Entry("src/b.cs"));
        view.SelectedFolder = view.Folders.Single(folder => folder.Content.RelPath == "art");

        view.SelectedFolder = null;

        await Assert.That(Keys(view)).IsEqualTo("art/a.png,src/b.cs");
    }

    [Test]
    public async Task Collapsing_a_folder_hides_the_lines_below_it_and_keeps_its_count()
    {
        var view = await NestedAsync();

        view.ToggleFolderCommand.Execute(Folder(view, "art"));

        await Assert.That(Folders(view)).IsEqualTo(":3,art:2,src:1");
        await Assert.That(Folder(view, "art").Content.IsCollapsed).IsTrue();
    }

    [Test]
    public async Task Toggling_a_collapsed_folder_shows_its_lines_again()
    {
        var view = await NestedAsync();
        view.ToggleFolderCommand.Execute(Folder(view, "art"));

        view.ToggleFolderCommand.Execute(Folder(view, "art"));

        await Assert.That(Folders(view)).IsEqualTo(":3,art:2,art/chars:1,src:1");
        await Assert.That(Folder(view, "art").Content.IsCollapsed).IsFalse();
    }

    [Test]
    public async Task Collapsing_a_folder_leaves_a_choice_it_does_not_hide_alone()
    {
        var view = await NestedAsync();
        var src = Folder(view, "src");
        view.SelectedFolder = src;

        view.ToggleFolderCommand.Execute(Folder(view, "art"));

        await Assert.That(view.SelectedFolder).IsSameReferenceAs(src);
        await Assert.That(Keys(view)).IsEqualTo("src/b.cs");
    }

    /// <summary>The table must never be narrowed by a line nobody can see.</summary>
    [Test]
    public async Task A_chosen_folder_hidden_by_a_collapse_hands_the_choice_to_the_collapsed_one()
    {
        var view = await NestedAsync();
        view.SelectedFolder = Folder(view, "art/chars");

        view.ToggleFolderCommand.Execute(Folder(view, "art"));

        await Assert.That(view.SelectedFolder).IsSameReferenceAs(Folder(view, "art"));
        await Assert.That(Keys(view)).IsEqualTo("art/a.png,art/chars/hero.png");
    }

    [Test]
    [Arguments("src")]
    [Arguments(null)]
    public async Task Toggling_a_folder_with_nothing_under_it_or_no_folder_changes_nothing(
        string? relPath
    )
    {
        var view = await NestedAsync();

        view.ToggleFolderCommand.Execute(relPath is null ? null : Folder(view, relPath));

        await Assert.That(Folders(view)).IsEqualTo(":3,art:2,art/chars:1,src:1");
        await Assert.That(view.Folders.Any(folder => folder.Content.IsCollapsed)).IsFalse();
    }

    [Test]
    public async Task A_collapsed_folder_stays_collapsed_across_a_resync()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Nested))
            .Answers(Listing([.. Nested, Entry("art/chars/villain.png")]));
        var view = View(status);
        await view.RefreshAsync(None);
        view.ToggleFolderCommand.Execute(Folder(view, "art"));

        await view.RefreshAsync(None);

        await Assert.That(Folders(view)).IsEqualTo(":4,art:3,src:1");
        await Assert.That(Folder(view, "art").Content.IsCollapsed).IsTrue();
    }

    [Test]
    public async Task A_collapsed_folder_that_leaves_the_tree_comes_back_expanded()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Nested))
            .Answers(Listing(Entry("src/b.cs")))
            .Answers(Listing(Nested));
        var view = View(status);
        await view.RefreshAsync(None);
        view.ToggleFolderCommand.Execute(Folder(view, "art"));

        await view.RefreshAsync(None);
        await view.RefreshAsync(None);

        await Assert.That(Folders(view)).IsEqualTo(":3,art:2,art/chars:1,src:1");
    }

    [Test]
    public async Task Stepping_out_of_an_open_folder_collapses_it_and_keeps_it_chosen()
    {
        var view = await NestedAsync();
        var art = Folder(view, "art");
        view.SelectedFolder = art;

        view.StepOutCommand.Execute(null);

        await Assert.That(art.Content.IsCollapsed).IsTrue();
        await Assert.That(view.SelectedFolder).IsSameReferenceAs(art);
    }

    [Test]
    [Arguments("art/chars", "art")]
    [Arguments("src", "")]
    public async Task Stepping_out_of_a_folder_with_nothing_under_it_chooses_its_parent(
        string from,
        string parent
    )
    {
        var view = await NestedAsync();
        view.SelectedFolder = Folder(view, from);

        view.StepOutCommand.Execute(null);

        await Assert.That(view.SelectedFolder).IsSameReferenceAs(Folder(view, parent));
        await Assert.That(Folders(view)).IsEqualTo(":3,art:2,art/chars:1,src:1");
    }

    [Test]
    public async Task Stepping_out_of_a_collapsed_folder_chooses_its_parent_and_leaves_it_collapsed()
    {
        var view = await NestedAsync();
        var art = Folder(view, "art");
        view.SelectedFolder = art;
        view.ToggleFolderCommand.Execute(art);

        view.StepOutCommand.Execute(null);

        await Assert.That(view.SelectedFolder).IsSameReferenceAs(view.Folders[0]);
        await Assert.That(art.Content.IsCollapsed).IsTrue();
    }

    [Test]
    public async Task Stepping_out_of_a_collapsed_root_goes_nowhere()
    {
        var view = await NestedAsync();
        view.ToggleFolderCommand.Execute(view.Folders[0]);

        view.StepOutCommand.Execute(null);

        await Assert.That(view.SelectedFolder).IsSameReferenceAs(view.Folders[0]);
        await Assert.That(Folders(view)).IsEqualTo(":3");
    }

    [Test]
    public async Task Stepping_into_a_collapsed_folder_expands_it()
    {
        var view = await NestedAsync();
        var art = Folder(view, "art");
        view.SelectedFolder = art;
        view.ToggleFolderCommand.Execute(art);

        view.StepInCommand.Execute(null);

        await Assert.That(art.Content.IsCollapsed).IsFalse();
        await Assert.That(Folders(view)).IsEqualTo(":3,art:2,art/chars:1,src:1");
    }

    [Test]
    [Arguments("art")]
    [Arguments("src")]
    public async Task Stepping_into_an_open_folder_or_one_with_nothing_under_it_changes_nothing(
        string relPath
    )
    {
        var view = await NestedAsync();
        var chosen = Folder(view, relPath);
        view.SelectedFolder = chosen;

        view.StepInCommand.Execute(null);

        await Assert.That(view.SelectedFolder).IsSameReferenceAs(chosen);
        await Assert.That(Folders(view)).IsEqualTo(":3,art:2,art/chars:1,src:1");
    }

    [Test]
    public async Task Stepping_with_nothing_chosen_changes_nothing()
    {
        var view = await ListedAsync();

        view.StepInCommand.Execute(null);
        view.StepOutCommand.Execute(null);

        await Assert.That(view.SelectedFolder).IsNull();
        await Assert.That(view.Folders).IsEmpty();
    }

    private static readonly WorkingCopyEntry[] Nested =
    [
        Entry("art/a.png"),
        Entry("art/chars/hero.png"),
        Entry("src/b.cs"),
    ];

    private static Task<WorkingCopyViewModel> NestedAsync() => ListedAsync(Nested);

    private static FolderEntry Folder(WorkingCopyViewModel view, string relPath) =>
        view.Folders.Single(folder => folder.Content.RelPath == relPath);

    private static async Task<WorkingCopyViewModel> ListedAsync(params WorkingCopyEntry[] entries)
    {
        var view = View(new FakeWorkingCopyStatus().Answers(Listing(entries)));
        await view.RefreshAsync(None);
        return view;
    }

    private static WorkingCopyViewModel View(FakeWorkingCopyStatus status) =>
        WorkingCopies.View(status);

    private static string Keys(WorkingCopyViewModel view) =>
        string.Join(",", view.Entries.Select(entry => entry.Key));

    private static string Folders(WorkingCopyViewModel view) =>
        string.Join(
            ",",
            view.Folders.Select(folder => $"{folder.Content.RelPath}:{folder.Content.Count}")
        );
}
