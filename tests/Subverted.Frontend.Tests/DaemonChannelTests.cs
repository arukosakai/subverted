namespace Subverted.Frontend.Tests;

/// <summary>
/// Every front-end starts the daemon from beside itself, so the name it looks for there is the one
/// thing that has to agree with what the publish step puts down.
/// </summary>
public sealed class DaemonChannelTests
{
    [Test]
    [Arguments(true, "subverted-daemon.exe")]
    [Arguments(false, "subverted-daemon")]
    public async Task The_daemon_is_looked_for_beside_the_front_end_under_the_platforms_name(
        bool isWindows,
        string expected
    )
    {
        var directory = Path.Combine(Path.GetTempPath(), "bin");

        await Assert
            .That(DaemonChannel.ExecutableNextTo(directory, isWindows))
            .IsEqualTo(Path.Combine(directory, expected));
    }
}
