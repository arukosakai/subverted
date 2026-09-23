namespace Subverted.App.ViewModels;

/// <summary>
/// Asks again on an interval, one request at a time. The daemon has no push channel, and does not
/// need one for this: a warm answer is about a millisecond, so asking every second while the window
/// is in front costs nothing and stopping when it is not costs even less.
/// </summary>
/// <remarks>
/// Ticks never overlap: the next wait starts only once the previous refresh has finished, so a
/// slow cold scan delays the following one rather than queueing a pile of them behind it.
/// </remarks>
public sealed class StatusPolling(
    TimeProvider clock,
    TimeSpan interval,
    Func<CancellationToken, Task> tick
) : IAsyncDisposable
{
    private CancellationTokenSource? _running;
    private Task? _loop;

    public bool IsRunning => _running is not null;

    /// <summary>Starts ticking; a second call while running does nothing.</summary>
    public void Start()
    {
        if (_running is not null)
        {
            return;
        }

        _running = new CancellationTokenSource();
        _loop = RunAsync(_running.Token);
    }

    /// <summary>Stops, and waits for a tick in flight to see its cancellation.</summary>
    public async Task StopAsync()
    {
        if (_running is not { } running)
        {
            return;
        }

        _running = null;
        await running.CancelAsync();
        await _loop!;
        running.Dispose();
        _loop = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    /// <summary>Ends only by being stopped: the timer is disposed after the loop, never during it.</summary>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval, clock);
        try
        {
            while (true)
            {
                await timer.WaitForNextTickAsync(cancellationToken);
                await tick(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped: the wait, or the tick in flight, saw the cancellation.
        }
    }
}
