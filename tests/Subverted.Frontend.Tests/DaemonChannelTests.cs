using Subverted.Protocol;

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

    [Test]
    public async Task A_daemon_that_is_not_there_is_an_io_failure_naming_it()
    {
        using var directory = new ScratchDirectory();
        var daemon = Path.Combine(directory.Path, "subverted-daemon");
        var channel = new DaemonChannel(Path.Combine(directory.Path, "none.sock"), daemon);

        var thrown = await Assert
            .That(async () =>
            {
                await channel.SendAsync(
                    new StatusRequest(".", false, false),
                    CancellationToken.None
                );
            })
            .Throws<FileNotFoundException>();

        await Assert.That(thrown!.Message).Contains(daemon);
    }

    /// <summary>
    /// A daemon blocked by antivirus or stripped of its exec bit throws Win32Exception from the
    /// spawn, which no front-end catches; it has to read as the daemon being unreachable.
    /// </summary>
    [Test]
    public async Task A_daemon_that_cannot_be_run_is_an_io_failure_naming_it()
    {
        using var directory = new ScratchDirectory();
        var daemon = Path.Combine(directory.Path, "subverted-daemon.exe");
        await File.WriteAllTextAsync(daemon, "not a program");
        var channel = new DaemonChannel(Path.Combine(directory.Path, "none.sock"), daemon);

        var thrown = await Assert
            .That(async () =>
            {
                await channel.SendAsync(
                    new StatusRequest(".", false, false),
                    CancellationToken.None
                );
            })
            .Throws<IOException>();

        await Assert.That(thrown!.Message).Contains(daemon);
        await Assert.That(thrown.InnerException).IsTypeOf<System.ComponentModel.Win32Exception>();
    }

    private sealed class ScratchDirectory : IDisposable
    {
        public string Path { get; } =
            Directory.CreateTempSubdirectory("subverted-channel-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
