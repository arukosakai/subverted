using Microsoft.Extensions.Time.Testing;
using Subverted.App.ViewModels;

namespace Subverted.App.Tests;

public sealed class MainWindowViewModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Test]
    public async Task First_run_shows_nothing_and_asks_nothing()
    {
        var status = new FakeWorkingCopyStatus();
        await using var window = Window(new FakeRecentStore(), status);

        await window.StartAsync(None);

        await Assert.That(window.HasWorkingCopy).IsFalse();
        await Assert.That(window.Recent).IsEmpty();
        await Assert.That(status.Reads).IsEqualTo(0);
    }

    [Test]
    public async Task Starting_reopens_the_most_recent_copy_and_answers_it_at_once()
    {
        var status = new FakeWorkingCopyStatus();
        await using var window = Window(new FakeRecentStore("/game", "/tools"), status);

        await window.StartAsync(None);

        await Assert.That(window.Current!.Path).IsEqualTo("/game");
        await Assert.That(window.Current.State).IsEqualTo(WorkingCopyState.Ready);
        await Assert.That(status.Reads).IsEqualTo(1);
        await Assert
            .That(window.Recent.Select(item => item.IsCurrent))
            .IsEquivalentTo(new[] { true, false });
    }

    [Test]
    public async Task Opening_a_folder_shows_it_and_remembers_it_first()
    {
        var store = new FakeRecentStore("/tools");
        await using var window = Window(store, new FakeWorkingCopyStatus(), picked: "/game");

        await window.OpenFolderCommand.ExecuteAsync(null);

        await Assert.That(window.Current!.Path).IsEqualTo("/game");
        await Assert.That(string.Join(",", store.Kept)).IsEqualTo("/game,/tools");
        await Assert.That(window.Recent[0].Name).IsEqualTo("game");
    }

    [Test]
    public async Task Cancelling_the_picker_changes_nothing()
    {
        var store = new FakeRecentStore("/tools");
        await using var window = Window(store, new FakeWorkingCopyStatus(), picked: null);

        await window.OpenFolderCommand.ExecuteAsync(null);

        await Assert.That(window.HasWorkingCopy).IsFalse();
        await Assert.That(store.Saves).IsEqualTo(0);
    }

    [Test]
    public async Task Selecting_a_recent_copy_shows_it()
    {
        await using var window = Window(
            new FakeRecentStore("/game", "/tools"),
            new FakeWorkingCopyStatus()
        );
        await window.StartAsync(None);

        await window.SelectCommand.ExecuteAsync(window.Recent[1]);

        await Assert.That(window.Current!.Path).IsEqualTo("/tools");
        await Assert.That(window.Recent[0].Path).IsEqualTo("/tools");
        await Assert.That(window.Recent[0].IsCurrent).IsTrue();
    }

    [Test]
    public async Task In_front_the_shown_copy_is_kept_current()
    {
        var clock = new FakeTimeProvider();
        var status = new FakeWorkingCopyStatus();
        await using var window = Window(new FakeRecentStore("/game"), status, clock: clock);
        await window.StartAsync(None);
        await window.ActivatedAsync(None);
        var afterActivation = status.Reads;

        // One interval at a time: a periodic timer folds ticks that pass unobserved into one.
        for (var second = 0; second < 3; second++)
        {
            clock.Advance(MainWindowViewModel.RefreshInterval);
            await Settle();
        }

        await Assert.That(status.Reads).IsEqualTo(afterActivation + 3);
    }

    /// <summary>Nobody is looking at a window in the back, so nobody is asked on its behalf.</summary>
    [Test]
    public async Task In_the_back_nothing_is_asked()
    {
        var clock = new FakeTimeProvider();
        var status = new FakeWorkingCopyStatus();
        await using var window = Window(new FakeRecentStore("/game"), status, clock: clock);
        await window.StartAsync(None);
        await window.ActivatedAsync(None);
        await window.DeactivatedAsync();
        var whenItWentBack = status.Reads;

        clock.Advance(MainWindowViewModel.RefreshInterval * 5);
        await Settle();

        await Assert.That(status.Reads).IsEqualTo(whenItWentBack);
    }

    /// <summary>
    /// Coming back to the front answers at once instead of waiting out an interval: the first thing
    /// someone sees after switching back from their editor should be what they just saved.
    /// </summary>
    [Test]
    public async Task Coming_back_to_the_front_answers_at_once()
    {
        var status = new FakeWorkingCopyStatus();
        await using var window = Window(new FakeRecentStore("/game"), status);
        await window.StartAsync(None);
        await window.ActivatedAsync(None);
        await window.DeactivatedAsync();
        var beforeReturning = status.Reads;

        await window.ActivatedAsync(None);

        await Assert.That(status.Reads).IsEqualTo(beforeReturning + 1);
    }

    /// <summary>The ordinary way in: the window is in front, and a folder is picked.</summary>
    [Test]
    public async Task A_copy_opened_in_front_is_kept_current_from_the_start()
    {
        var clock = new FakeTimeProvider();
        var status = new FakeWorkingCopyStatus();
        await using var window = Window(
            new FakeRecentStore(),
            status,
            picked: "/game",
            clock: clock
        );
        await window.StartAsync(None);
        await window.ActivatedAsync(None);

        await window.OpenFolderCommand.ExecuteAsync(null);
        clock.Advance(MainWindowViewModel.RefreshInterval);
        await Settle();

        await Assert.That(status.Reads).IsEqualTo(2);
    }

    [Test]
    public async Task Activating_again_while_live_does_not_ask_twice()
    {
        var status = new FakeWorkingCopyStatus();
        await using var window = Window(new FakeRecentStore("/game"), status);
        await window.StartAsync(None);
        await window.ActivatedAsync(None);
        var afterFirst = status.Reads;

        await window.ActivatedAsync(None);

        await Assert.That(status.Reads).IsEqualTo(afterFirst);
    }

    /// <summary>
    /// The window comes to the front while a copy's first answer is still on its way. The copy is
    /// already current but not yet polled; it must end up polled once the answer lands.
    /// </summary>
    [Test]
    public async Task Coming_to_the_front_during_the_first_answer_still_ends_up_live()
    {
        var clock = new FakeTimeProvider();
        var status = new GatedWorkingCopyStatus();
        await using var window = new MainWindowViewModel(
            new FakeRecentStore(),
            new FakeFolderPicker("/game"),
            path => new WorkingCopyViewModel(path, status, DiffPanes.Pane()),
            clock,
            StringComparison.Ordinal
        );

        var opening = window.OpenFolderCommand.ExecuteAsync(null);
        await window.ActivatedAsync(None);
        status.Release();
        await opening;
        clock.Advance(MainWindowViewModel.RefreshInterval);
        await Settle();

        await Assert.That(status.Reads).IsEqualTo(2);
    }

    [Test]
    public async Task Activating_with_nothing_open_asks_nothing()
    {
        var status = new FakeWorkingCopyStatus();
        await using var window = Window(new FakeRecentStore(), status);
        await window.StartAsync(None);

        await window.ActivatedAsync(None);

        await Assert.That(status.Reads).IsEqualTo(0);
    }

    /// <summary>A copy opened while the window is in the back waits to be looked at.</summary>
    [Test]
    public async Task A_copy_opened_in_the_back_is_not_polled_until_the_window_comes_forward()
    {
        var clock = new FakeTimeProvider();
        var status = new FakeWorkingCopyStatus();
        await using var window = Window(new FakeRecentStore("/game"), status, clock: clock);

        await window.StartAsync(None);
        clock.Advance(MainWindowViewModel.RefreshInterval * 3);
        await Settle();

        await Assert.That(status.Reads).IsEqualTo(1);
    }

    private static MainWindowViewModel Window(
        FakeRecentStore store,
        FakeWorkingCopyStatus status,
        string? picked = null,
        TimeProvider? clock = null
    ) =>
        new(
            store,
            new FakeFolderPicker(picked),
            path => new WorkingCopyViewModel(path, status, DiffPanes.Pane()),
            clock ?? new FakeTimeProvider(),
            StringComparison.Ordinal
        );

    private static async Task Settle()
    {
        for (var turn = 0; turn < 10; turn++)
        {
            await Task.Yield();
            await Task.Delay(1);
        }
    }
}
