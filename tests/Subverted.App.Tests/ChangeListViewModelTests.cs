using Subverted.App.ViewModels;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>The condensed list: order, filter, layout, ticks and what a line's menu does.</summary>
public sealed class ChangeListViewModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeFileLauncher _launcher = new();
    private readonly FakeFileRevealer _revealer = new();
    private readonly FakeTextClipboard _clipboard = new();

    [Test]
    public async Task Conflicted_and_missing_lines_are_pinned_above_the_rest()
    {
        var view = await ListedAsync(
            Entry("a.png"),
            Entry("z/gone.png", NodeStatus.Missing),
            Entry("m/clash.txt", NodeStatus.Conflicted)
        );

        await Assert.That(Keys(view)).IsEqualTo("m/clash.txt,z/gone.png,a.png");
        await Assert.That(view.IsFlat).IsTrue();
        await Assert.That(view.IsTree).IsFalse();
    }

    [Test]
    public async Task The_tree_nests_the_rest_under_folders_and_keeps_the_pins_on_top()
    {
        var view = await ListedAsync(Entry("art/a.png"), Entry("z/gone.png", NodeStatus.Missing));

        view.ShowTreeCommand.Execute(null);

        await Assert.That(Keys(view)).IsEqualTo("z/gone.png,art/,art/a.png");
        await Assert.That(view.IsTree).IsTrue();
        await Assert.That(view.IsFlat).IsFalse();
    }

    [Test]
    public async Task Back_to_flat_drops_the_folder_lines()
    {
        var view = await ListedAsync(Entry("art/a.png"));
        view.ShowTreeCommand.Execute(null);

        view.ShowFlatCommand.Execute(null);

        await Assert.That(Keys(view)).IsEqualTo("art/a.png");
        await Assert.That(view.Entries[0].Content.Folder).IsEqualTo("art");
    }

    [Test]
    public async Task The_filter_shows_only_matching_lines_and_counts_the_rest()
    {
        var view = await ListedAsync(Entry("art/a.png"), Entry("src/b.cs"), Entry("src/c.cs"));

        view.Filter = "art";

        await Assert.That(Keys(view)).IsEqualTo("art/a.png");
        await Assert.That(view.HiddenText).IsEqualTo("2 changes hidden");
    }

    /// <summary>The header answers for the working copy, not for what the filter lets through.</summary>
    [Test]
    public async Task The_summary_still_counts_what_the_filter_hides()
    {
        var view = await ListedAsync(Entry("art/a.png"), Entry("src/b.cs", NodeStatus.Added));

        view.Filter = "art";

        await Assert
            .That(string.Join(",", view.Summary.Select(count => count.Text)))
            .IsEqualTo("1 modified,1 added");
        await Assert.That(view.Changes.Count).IsEqualTo(2);
    }

    [Test]
    public async Task With_no_filter_nothing_is_said_to_be_hidden()
    {
        var view = await ListedAsync(Entry("art/a.png"));

        await Assert.That(view.HiddenText).IsNull();
    }

    [Test]
    public async Task Clearing_the_filter_shows_everything_again()
    {
        var view = await ListedAsync(Entry("art/a.png"), Entry("src/b.cs"));
        view.Filter = "art";

        view.ClearFilterCommand.Execute(null);

        await Assert.That(view.Filter).IsEqualTo("");
        await Assert.That(Keys(view)).IsEqualTo("art/a.png,src/b.cs");
        await Assert.That(view.HiddenText).IsNull();
    }

    [Test]
    public async Task The_filter_holds_through_a_resync()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("art/a.png"), Entry("src/b.cs")))
            .Answers(Listing(Entry("art/a.png"), Entry("src/b.cs"), Entry("art/c.png")));
        var view = View(status);
        await view.RefreshAsync(None);
        view.Filter = "art";

        await view.RefreshAsync(None);

        await Assert.That(Keys(view)).IsEqualTo("art/a.png,art/c.png");
        await Assert.That(view.HiddenText).IsEqualTo("1 change hidden");
    }

    [Test]
    public async Task Ticking_a_line_ticks_its_path()
    {
        var view = await ListedAsync(Entry("a.png"), Entry("b.png"));

        view.ToggleTickCommand.Execute(view.Entries[1]);

        await Assert.That(view.Entries[1].IsTicked).IsTrue();
        await Assert.That(view.Entries[0].IsTicked).IsFalse();
        await Assert.That(view.Ticked).IsEquivalentTo(new[] { "b.png" });
    }

    [Test]
    public async Task Ticking_it_again_unticks_it()
    {
        var view = await ListedAsync(Entry("a.png"));
        view.ToggleTickCommand.Execute(view.Entries[0]);

        view.ToggleTickCommand.Execute(view.Entries[0]);

        await Assert.That(view.Entries[0].IsTicked).IsFalse();
        await Assert.That(view.Ticked).IsEmpty();
    }

    /// <summary>The line is updated in place by the resync; its tick is kept beside it and put back.</summary>
    [Test]
    public async Task A_tick_survives_a_resync_that_changed_its_line()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("a.png")))
            .Answers(Listing(Entry("a.png", NodeStatus.Conflicted)));
        var view = View(status);
        await view.RefreshAsync(None);
        view.ToggleTickCommand.Execute(view.Entries[0]);

        await view.RefreshAsync(None);

        await Assert.That(view.Entries[0].IsTicked).IsTrue();
        await Assert.That(view.Ticked).IsEquivalentTo(new[] { "a.png" });
    }

    [Test]
    public async Task A_tick_survives_the_filter_hiding_its_line_and_is_shown_when_it_comes_back()
    {
        var view = await ListedAsync(Entry("art/a.png"), Entry("src/b.cs"));
        view.ToggleTickCommand.Execute(view.Entries[0]);

        view.Filter = "src";
        var whileHidden = view.Ticked.ToList();
        view.Filter = "";

        await Assert.That(whileHidden).IsEquivalentTo(new[] { "art/a.png" });
        await Assert.That(view.Entries[0].IsTicked).IsTrue();
    }

    [Test]
    public async Task A_tick_survives_switching_to_the_tree()
    {
        var view = await ListedAsync(Entry("art/a.png"));
        view.ToggleTickCommand.Execute(view.Entries[0]);

        view.ShowTreeCommand.Execute(null);

        await Assert.That(view.Entries.Single(entry => entry.Key == "art/a.png").IsTicked).IsTrue();
        await Assert.That(view.Entries.Single(entry => entry.Key == "art/").IsTicked).IsFalse();
    }

    /// <summary>Committed from elsewhere, then edited again: a new change, which must not come back ticked.</summary>
    [Test]
    public async Task A_tick_is_dropped_when_its_path_leaves_the_listing()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("a.png"), Entry("b.png")))
            .Answers(Listing(Entry("b.png")))
            .Answers(Listing(Entry("a.png"), Entry("b.png")));
        var view = View(status);
        await view.RefreshAsync(None);
        view.ToggleTickCommand.Execute(view.Entries[0]);

        await view.RefreshAsync(None);
        var afterLeaving = view.Ticked.Count;
        await view.RefreshAsync(None);

        await Assert.That(afterLeaving).IsEqualTo(0);
        await Assert.That(view.Entries[0].IsTicked).IsFalse();
    }

    /// <summary>A failed refresh says nothing about what is listed, so it forgets no tick.</summary>
    [Test]
    public async Task A_failed_refresh_keeps_every_tick()
    {
        var status = new FakeWorkingCopyStatus().Answers(Listing(Entry("a.png"))).IsUnreachable();
        var view = View(status);
        await view.RefreshAsync(None);
        view.ToggleTickCommand.Execute(view.Entries[0]);

        await view.RefreshAsync(None);

        await Assert.That(view.Ticked).IsEquivalentTo(new[] { "a.png" });
    }

    [Test]
    public async Task A_folder_line_has_nothing_to_tick_or_open()
    {
        var view = await ListedAsync(Entry("art/a.png"));
        view.ShowTreeCommand.Execute(null);
        var folder = view.Entries[0];

        await Assert.That(folder.CanTick).IsFalse();
        await Assert.That(view.ToggleTickCommand.CanExecute(folder)).IsFalse();
        await Assert.That(view.OpenCommand.CanExecute(folder)).IsFalse();
        await Assert.That(view.ToggleTickCommand.CanExecute(view.Entries[1])).IsTrue();
        await Assert.That(view.OpenCommand.CanExecute(view.Entries[1])).IsTrue();
    }

    [Test]
    public async Task No_line_has_nothing_to_act_on()
    {
        var view = await ListedAsync(Entry("a.png"));

        await Assert.That(view.ToggleTickCommand.CanExecute(null)).IsFalse();
        await Assert.That(view.OpenCommand.CanExecute(null)).IsFalse();
        await Assert.That(view.RevealCommand.CanExecute(null)).IsFalse();
        await Assert.That(view.CopyPathCommand.CanExecute(null)).IsFalse();
        await Assert.That(view.ShowHistoryCommand.CanExecute(null)).IsFalse();
    }

    [Test]
    public async Task Opening_a_line_hands_its_absolute_path_to_the_system()
    {
        var view = await ListedAsync(Entry("art/hero.png"));

        await view.OpenCommand.ExecuteAsync(view.Entries[0]);

        await Assert
            .That(_launcher.Opened)
            .IsEquivalentTo(new[] { DiffTarget.PathOf(Info.RootPath, "art/hero.png") });
    }

    [Test]
    public async Task Revealing_a_change_shows_its_absolute_path()
    {
        var view = await ListedAsync(Entry("art/hero.png"));

        await view.RevealCommand.ExecuteAsync(view.Entries[0]);

        await Assert
            .That(_revealer.Revealed)
            .IsEquivalentTo(new[] { DiffTarget.PathOf(Info.RootPath, "art/hero.png") });
    }

    /// <summary>A folder line's path is the folder, not its key with the trailing slash.</summary>
    [Test]
    public async Task Revealing_a_folder_line_shows_the_folder()
    {
        var view = await ListedAsync(Entry("art/hero.png"));
        view.ShowTreeCommand.Execute(null);

        await view.RevealCommand.ExecuteAsync(view.Entries[0]);

        await Assert
            .That(_revealer.Revealed)
            .IsEquivalentTo(new[] { DiffTarget.PathOf(Info.RootPath, "art") });
    }

    [Test]
    public async Task Copying_a_line_s_path_copies_the_absolute_path()
    {
        var view = await ListedAsync(Entry("art/hero.png"));

        await view.CopyPathCommand.ExecuteAsync(view.Entries[0]);

        await Assert
            .That(_clipboard.Copied)
            .IsEquivalentTo(new[] { DiffTarget.PathOf(Info.RootPath, "art/hero.png") });
    }

    /// <summary>History is built elsewhere; until it listens, the menu item says it cannot be used.</summary>
    [Test]
    public async Task History_cannot_be_asked_for_until_something_listens()
    {
        var view = await ListedAsync(Entry("a.png"));

        await Assert.That(view.ShowHistoryCommand.CanExecute(view.Entries[0])).IsFalse();
    }

    [Test]
    public async Task Asking_for_history_raises_the_change_s_absolute_path()
    {
        var view = await ListedAsync(Entry("art/hero.png"));
        var asked = new List<string>();
        view.HistoryRequested += asked.Add;

        view.ShowHistoryCommand.Execute(view.Entries[0]);

        await Assert
            .That(asked)
            .IsEquivalentTo(new[] { DiffTarget.PathOf(Info.RootPath, "art/hero.png") });
    }

    [Test]
    public async Task Listening_and_stopping_turn_the_history_item_on_and_off()
    {
        var view = await ListedAsync(Entry("a.png"));
        var raised = 0;
        view.ShowHistoryCommand.CanExecuteChanged += (_, _) => raised++;
        Action<string> listener = _ => { };

        view.HistoryRequested += listener;
        var whileListening = view.ShowHistoryCommand.CanExecute(view.Entries[0]);
        view.HistoryRequested -= listener;

        await Assert.That(whileListening).IsTrue();
        await Assert.That(view.ShowHistoryCommand.CanExecute(view.Entries[0])).IsFalse();
        await Assert.That(raised).IsEqualTo(2);
    }

    [Test]
    public async Task A_change_with_no_history_and_a_folder_line_cannot_ask_for_it()
    {
        var view = await ListedAsync(Entry("art/new.png", NodeStatus.Added));
        view.HistoryRequested += _ => { };
        view.ShowTreeCommand.Execute(null);

        await Assert.That(view.ShowHistoryCommand.CanExecute(view.Entries[1])).IsFalse();
        await Assert.That(view.ShowHistoryCommand.CanExecute(view.Entries[0])).IsFalse();
    }

    /// <summary>A line's change can gain history under the same entry — an add committed then edited.</summary>
    [Test]
    public async Task A_resync_tells_the_menu_that_what_a_line_allows_may_have_changed()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("a.png", NodeStatus.Added)))
            .Answers(Listing(Entry("a.png")));
        var view = View(status);
        await view.RefreshAsync(None);
        var raised = new List<string>();
        view.ToggleTickCommand.CanExecuteChanged += (_, _) => raised.Add("tick");
        view.OpenCommand.CanExecuteChanged += (_, _) => raised.Add("open");
        view.ShowHistoryCommand.CanExecuteChanged += (_, _) => raised.Add("history");

        await view.RefreshAsync(None);

        await Assert.That(raised).IsEquivalentTo(new[] { "tick", "open", "history" });
    }

    private async Task<WorkingCopyViewModel> ListedAsync(params WorkingCopyEntry[] entries)
    {
        var view = View(new FakeWorkingCopyStatus().Answers(Listing(entries)));
        await view.RefreshAsync(None);
        return view;
    }

    private WorkingCopyViewModel View(FakeWorkingCopyStatus status) =>
        WorkingCopies.View(status, launcher: _launcher, revealer: _revealer, clipboard: _clipboard);

    private static string Keys(WorkingCopyViewModel view) =>
        string.Join(",", view.Entries.Select(entry => entry.Key));
}
