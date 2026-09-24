using Microsoft.Extensions.Time.Testing;
using Subverted.App.Presentation;
using Subverted.App.ViewModels;
using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.App.Tests;

/// <summary>The sidebar's VIEW switch, and the entry point for one path's history.</summary>
public sealed class MainWindowHistoryTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeRevisionHistory _history = new();

    [Test]
    public async Task A_working_copy_opens_on_its_changes()
    {
        await using var window = await Opened("/game");

        await Assert.That(window.ShownView).IsEqualTo(WorkspaceView.Changes);
        await Assert.That(window.IsShowingChanges).IsTrue();
        await Assert.That(window.IsShowingHistory).IsFalse();
        await Assert.That(_history.Pages).IsEmpty();
    }

    [Test]
    public async Task Switching_to_history_reads_the_whole_copys_history_from_its_root()
    {
        await using var window = await Opened("/game/art");

        await window.ShowHistoryCommand.ExecuteAsync(null);

        await Assert.That(window.IsShowingHistory).IsTrue();
        await Assert.That(window.IsShowingChanges).IsFalse();
        await Assert.That(window.History.Path).IsEqualTo("/game");
        await Assert.That(_history.Pages.Select(page => page.Path)).IsEquivalentTo(["/game"]);
    }

    [Test]
    public async Task Switching_back_and_forth_reads_the_history_only_once()
    {
        await using var window = await Opened("/game");

        await window.ShowHistoryCommand.ExecuteAsync(null);
        window.ShowChangesCommand.Execute(null);
        await window.ShowHistoryCommand.ExecuteAsync(null);

        await Assert.That(window.IsShowingHistory).IsTrue();
        await Assert.That(_history.Pages.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Opening_another_copy_returns_to_changes_and_its_history_is_read_fresh()
    {
        await using var window = await Opened("/game");
        await window.ShowHistoryCommand.ExecuteAsync(null);

        await window.ShowAsync("/tools", None);

        await Assert.That(window.ShownView).IsEqualTo(WorkspaceView.Changes);

        await window.ShowHistoryCommand.ExecuteAsync(null);

        await Assert.That(window.History.Path).IsEqualTo("/tools");
        await Assert
            .That(_history.Pages.Select(page => page.Path))
            .IsEquivalentTo(["/game", "/tools"]);
    }

    [Test]
    public async Task One_paths_history_is_shown_on_its_own_and_the_copys_is_read_again_afterwards()
    {
        await using var window = await Opened("/game");

        await window.ShowHistoryOfAsync("/game/art/hero.png", None);

        await Assert.That(window.IsShowingHistory).IsTrue();
        await Assert.That(window.History.Path).IsEqualTo("/game/art/hero.png");

        window.ShowChangesCommand.Execute(null);
        await window.ShowHistoryCommand.ExecuteAsync(null);

        await Assert.That(window.History.Path).IsEqualTo("/game");
    }

    [Test]
    public async Task A_rows_history_menu_item_shows_that_files_history()
    {
        await using var window = await Opened("/game");
        var row = new ChangeListEntry(
            ChangeListItem.Flat(ChangeRow.From(Entries.Entry("art/hero.png")))
        );

        window.Current!.ShowHistoryCommand.Execute(row);
        await Task.Yield();

        await Assert.That(window.IsShowingHistory).IsTrue();
        await Assert
            .That(window.History.Path)
            .IsEqualTo(DiffTarget.PathOf("/game", "art/hero.png"));
    }

    [Test]
    public async Task A_copy_opened_in_the_window_offers_its_rows_history()
    {
        await using var window = await Opened("/game");
        var row = new ChangeListEntry(
            ChangeListItem.Flat(ChangeRow.From(Entries.Entry("art/hero.png")))
        );

        await Assert.That(window.Current!.ShowHistoryCommand.CanExecute(row)).IsTrue();
    }

    [Test]
    public async Task The_pinned_local_changes_row_switches_back_to_changes()
    {
        await using var window = await Opened("/game");
        await window.ShowHistoryCommand.ExecuteAsync(null);

        window.History.ReturnToChangesCommand.Execute(null);

        await Assert.That(window.IsShowingChanges).IsTrue();
    }

    [Test]
    public async Task With_no_working_copy_open_there_is_no_history_to_switch_to()
    {
        await using var window = Window();

        await window.ShowHistoryCommand.ExecuteAsync(null);

        await Assert.That(window.IsShowingChanges).IsTrue();
        await Assert.That(_history.Pages).IsEmpty();
    }

    private async Task<MainWindowViewModel> Opened(string path)
    {
        var window = Window();
        await window.ShowAsync(path, None);
        return window;
    }

    private MainWindowViewModel Window()
    {
        return new MainWindowViewModel(
            new FakeRecentStore(),
            new FakeFolderPicker(null),
            path => WorkingCopies.View(new RootedStatus(path), path: path),
            new FakeTimeProvider(),
            StringComparison.Ordinal,
            Revisions.View(_history)
        );
    }

    private static StatusResponse Listing(string root) =>
        new(new WorkingCopyInfo(root, "file:///repo", "uuid", 31), [], true, 0.1, 0, []);

    /// <summary>Answers every status with the opened path's root: <c>/game/art</c> is inside <c>/game</c>.</summary>
    private sealed class RootedStatus(string opened) : IWorkingCopyStatus
    {
        public Task<DaemonResponse> ReadAsync(
            string workingCopyPath,
            ListedNodes listed,
            Guid? heldScan,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<DaemonResponse>(
                Listing(opened.StartsWith("/game", StringComparison.Ordinal) ? "/game" : opened)
            );
    }
}
