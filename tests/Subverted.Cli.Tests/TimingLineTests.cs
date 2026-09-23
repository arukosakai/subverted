using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Cli.Tests;

public sealed class TimingLineTests
{
    /// <summary>
    /// The split is the whole point of the line. M1's criterion is about the daemon's share, and a
    /// total that hides a 400 ms process start behind a 0.4 ms answer reads as a failure it is not.
    /// </summary>
    [Test]
    public async Task The_daemons_share_and_this_processs_share_are_reported_apart()
    {
        var line = TimingLine.For(Response(warm: true, serverMilliseconds: 0.4), 480);

        await Assert.That(line).Contains("daemon 0.4 ms");
        await Assert.That(line).Contains("sv 480 ms");
        await Assert.That(line).Contains("total 480 ms");
    }

    [Test]
    [Arguments(true, "warm")]
    [Arguments(false, "cold")]
    public async Task Whether_the_index_was_warm_is_said_in_a_word(bool warm, string expected)
    {
        var line = TimingLine.For(Response(warm, serverMilliseconds: 2150), 2200);

        await Assert.That(line).Contains(expected);
    }

    [Test]
    public async Task A_slow_answer_is_attributed_to_the_daemon_and_not_to_startup()
    {
        var line = TimingLine.For(Response(warm: false, serverMilliseconds: 2150), 2600);

        await Assert.That(line).Contains("daemon 2150.0 ms");
        await Assert.That(line).Contains("sv 450 ms");
    }

    private static StatusResponse Response(bool warm, double serverMilliseconds) =>
        new(
            new WorkingCopyInfo("/wc", "https://svn.example/repo", "uuid-1", 31),
            [],
            warm,
            serverMilliseconds,
            UnfinishedOperations: 0,
            UnrecordedMoves: []
        );
}
