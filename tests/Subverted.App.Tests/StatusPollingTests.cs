using Microsoft.Extensions.Time.Testing;
using Subverted.App.ViewModels;

namespace Subverted.App.Tests;

/// <summary>Driven by a fake clock, so "once a second" is asserted without anybody waiting one.</summary>
public sealed class StatusPollingTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    [Test]
    public async Task Nothing_is_asked_before_the_first_interval_has_passed()
    {
        var clock = new FakeTimeProvider();
        var ticks = 0;
        await using var polling = new StatusPolling(clock, Interval, _ => Task.FromResult(ticks++));

        polling.Start();
        clock.Advance(Interval - TimeSpan.FromMilliseconds(1));
        await Settle();

        await Assert.That(ticks).IsEqualTo(0);
    }

    [Test]
    public async Task Each_interval_asks_once()
    {
        var clock = new FakeTimeProvider();
        var ticks = 0;
        await using var polling = new StatusPolling(clock, Interval, _ => Task.FromResult(ticks++));

        polling.Start();
        for (var second = 0; second < 3; second++)
        {
            clock.Advance(Interval);
            await Settle();
        }

        await Assert.That(ticks).IsEqualTo(3);
    }

    [Test]
    public async Task Stopped_it_asks_nothing_more()
    {
        var clock = new FakeTimeProvider();
        var ticks = 0;
        var polling = new StatusPolling(clock, Interval, _ => Task.FromResult(ticks++));

        polling.Start();
        clock.Advance(Interval);
        await Settle();
        await polling.StopAsync();
        clock.Advance(Interval * 5);
        await Settle();

        await Assert.That(ticks).IsEqualTo(1);
        await Assert.That(polling.IsRunning).IsFalse();
    }

    /// <summary>Two loops would ask twice a second, and the window calls start on every activation.</summary>
    [Test]
    public async Task Starting_twice_runs_one_loop()
    {
        var clock = new FakeTimeProvider();
        var ticks = 0;
        await using var polling = new StatusPolling(clock, Interval, _ => Task.FromResult(ticks++));

        polling.Start();
        polling.Start();
        clock.Advance(Interval);
        await Settle();

        await Assert.That(ticks).IsEqualTo(1);
    }

    /// <summary>
    /// A cold scan can take longer than the interval. The next tick waits for it rather than
    /// queueing up behind it.
    /// </summary>
    [Test]
    public async Task A_slow_answer_is_never_overlapped_by_the_next_question()
    {
        var clock = new FakeTimeProvider();
        var inFlight = 0;
        var mostAtOnce = 0;
        var release = new TaskCompletionSource();
        await using var polling = new StatusPolling(
            clock,
            Interval,
            async _ =>
            {
                mostAtOnce = Math.Max(mostAtOnce, ++inFlight);
                await release.Task;
                inFlight--;
            }
        );

        polling.Start();
        clock.Advance(Interval);
        await Settle();
        clock.Advance(Interval * 3);
        await Settle();
        release.SetResult();
        await Settle();

        await Assert.That(mostAtOnce).IsEqualTo(1);
    }

    [Test]
    public async Task Stopping_what_never_started_is_harmless()
    {
        var polling = new StatusPolling(new FakeTimeProvider(), Interval, _ => Task.CompletedTask);

        await polling.StopAsync();

        await Assert.That(polling.IsRunning).IsFalse();
    }

    /// <summary>Lets the loop's continuations run after the clock moved; the fake clock fires inline.</summary>
    private static async Task Settle()
    {
        for (var turn = 0; turn < 5; turn++)
        {
            await Task.Yield();
            await Task.Delay(1);
        }
    }
}
