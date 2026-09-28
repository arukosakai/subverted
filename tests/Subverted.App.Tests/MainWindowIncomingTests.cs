using Microsoft.Extensions.Time.Testing;
using Subverted.App.ViewModels;

namespace Subverted.App.Tests;

/// <summary>
/// When the window asks the server what an update would bring: at once when a copy opens in front,
/// on coming back to the front unless it asked a moment ago, and on a slow timer while it stays
/// there — never from the back.
/// </summary>
public sealed class MainWindowIncomingTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeTimeProvider _clock = new();
    private readonly FakeWorkingCopyUpdate _updates = new();

    [Test]
    public async Task Opening_a_copy_in_front_asks_at_once()
    {
        await using var window = Window();
        await window.ActivatedAsync(None);

        await window.ShowAsync("/game", None);

        await Assert.That(_updates.AskedIncoming).IsEquivalentTo(new[] { "/game" });
        await Assert.That(window.Current!.Updater.Incoming).IsEqualTo(0);
    }

    [Test]
    public async Task Opening_a_copy_in_the_back_asks_nothing()
    {
        await using var window = Window();

        await window.ShowAsync("/game", None);

        await Assert.That(_updates.AskedIncoming).IsEmpty();
    }

    [Test]
    public async Task Coming_to_the_front_asks_for_the_copy_opened_in_the_back()
    {
        await using var window = Window();
        await window.ShowAsync("/game", None);

        await window.ActivatedAsync(None);

        await Assert.That(_updates.AskedIncoming).IsEquivalentTo(new[] { "/game" });
    }

    [Test]
    public async Task Each_interval_in_front_asks_again()
    {
        await using var window = Window();
        await window.ActivatedAsync(None);
        await window.ShowAsync("/game", None);

        for (var tick = 0; tick < 2; tick++)
        {
            _clock.Advance(MainWindowViewModel.IncomingInterval);
            await Settle();
        }

        await Assert.That(_updates.AskedIncoming.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Just_short_of_the_interval_nothing_more_is_asked()
    {
        await using var window = Window();
        await window.ActivatedAsync(None);
        await window.ShowAsync("/game", None);

        _clock.Advance(MainWindowViewModel.IncomingInterval - TimeSpan.FromSeconds(1));
        await Settle();

        await Assert.That(_updates.AskedIncoming.Count).IsEqualTo(1);
    }

    [Test]
    public async Task In_the_back_the_timer_asks_nothing()
    {
        await using var window = Window();
        await window.ActivatedAsync(None);
        await window.ShowAsync("/game", None);
        await window.DeactivatedAsync();

        _clock.Advance(MainWindowViewModel.IncomingInterval * 3);
        await Settle();

        await Assert.That(_updates.AskedIncoming.Count).IsEqualTo(1);
    }

    /// <summary>Alt-tabbing to an editor and back should not be a server round trip each time.</summary>
    [Test]
    public async Task Coming_back_while_the_last_answer_is_fresh_does_not_ask_again()
    {
        await using var window = Window();
        await window.ActivatedAsync(None);
        await window.ShowAsync("/game", None);
        await window.DeactivatedAsync();

        _clock.Advance(MainWindowViewModel.IncomingFreshFor - TimeSpan.FromSeconds(1));
        await window.ActivatedAsync(None);

        await Assert.That(_updates.AskedIncoming.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Coming_back_once_the_last_answer_is_stale_asks_again()
    {
        await using var window = Window();
        await window.ActivatedAsync(None);
        await window.ShowAsync("/game", None);
        await window.DeactivatedAsync();

        _clock.Advance(MainWindowViewModel.IncomingFreshFor);
        await window.ActivatedAsync(None);

        await Assert.That(_updates.AskedIncoming.Count).IsEqualTo(2);
    }

    /// <summary>A fresh answer is about the copy it was asked for, not whichever is open next.</summary>
    [Test]
    public async Task Opening_another_copy_asks_about_it_however_recently_the_last_was_asked()
    {
        await using var window = Window();
        await window.ActivatedAsync(None);
        await window.ShowAsync("/game", None);

        await window.ShowAsync("/tools", None);

        await Assert.That(_updates.AskedIncoming).IsEquivalentTo(new[] { "/game", "/tools" });
    }

    private MainWindowViewModel Window() =>
        new(
            new FakeRecentStore(),
            new FakeFolderPicker(null),
            path => WorkingCopies.View(new FakeWorkingCopyStatus(), path: path, updates: _updates),
            _clock,
            StringComparison.Ordinal,
            Revisions.View()
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
