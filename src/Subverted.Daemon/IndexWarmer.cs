using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Subverted.Daemon;

/// <summary>
/// Rescans working copies that <see cref="RescanSchedule"/> says are due, so the scan has usually
/// finished before anyone asks. This is what makes a warm answer the common case rather than the
/// lucky one.
/// </summary>
public sealed class IndexWarmer(
    WorkingCopySessions sessions,
    TimeProvider clock,
    ILogger<IndexWarmer> logger
) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(250);

    private readonly RescanSchedule _schedule = new(TimeSpan.FromMilliseconds(500));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var ticker = new PeriodicTimer(Tick, clock);
        try
        {
            while (await ticker.WaitForNextTickAsync(stoppingToken))
            {
                foreach (var session in sessions.All)
                {
                    await WarmIfDueAsync(session, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    private async Task WarmIfDueAsync(
        WorkingCopySession session,
        CancellationToken cancellationToken
    )
    {
        if (
            !_schedule.IsDue(
                session.Info.RootPath,
                session.Generation,
                session.IsStale,
                clock.GetUtcNow()
            )
        )
        {
            return;
        }

        try
        {
            await session.CurrentAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A working copy deleted or locked under us must not take the daemon down with it;
            // the next request will surface the failure to whoever asked.
            logger.LogWarning(ex, "Background rescan of {Root} failed.", session.Info.RootPath);
        }
    }
}
