using Subverted.Protocol;

namespace Subverted.Cli.Tests;

public sealed class DaemonInfoReportTests
{
    [Test]
    public async Task A_daemon_holding_nothing_says_so_instead_of_printing_an_empty_table()
    {
        var lines = DaemonInfoReport.Lines(new DaemonInfoResponse("1.2.3", 90, []));

        await Assert.That(lines.Count).IsEqualTo(2);
        await Assert.That(lines[0]).Contains("1.2.3");
        await Assert.That(lines[0]).Contains("90s");
        await Assert.That(lines[1]).IsEqualTo("no working copies held yet");
    }

    /// <summary>
    /// The one fact this command exists to surface. "NOT WATCHED" is shouted on purpose: a working
    /// copy nothing is watching is answered from a rescan every time, and the user should know why
    /// their status went slow (D5).
    /// </summary>
    [Test]
    [Arguments(WatcherState.Healthy, "watching")]
    [Arguments(WatcherState.Recovering, "rescanning")]
    [Arguments(WatcherState.Unavailable, "NOT WATCHED")]
    public async Task Each_watcher_state_is_named_in_words(WatcherState state, string expected)
    {
        var lines = DaemonInfoReport.Lines(
            new DaemonInfoResponse("1.0", 5, [new WatchedWorkingCopy("/wc", 20201, state)])
        );

        await Assert.That(lines[^1]).Contains(expected);
    }

    [Test]
    public async Task Each_working_copy_gets_its_node_count_and_its_root()
    {
        var lines = DaemonInfoReport.Lines(
            new DaemonInfoResponse(
                "1.0",
                5,
                [
                    new WatchedWorkingCopy("/wc", 20201, WatcherState.Healthy),
                    new WatchedWorkingCopy("/other", 7, WatcherState.Healthy),
                ]
            )
        );

        await Assert.That(lines.Count).IsEqualTo(4);
        await Assert.That(lines[2]).Contains("20201");
        await Assert.That(lines[2]).Contains("/wc");
        await Assert.That(lines[3]).Contains("/other");
    }
}
