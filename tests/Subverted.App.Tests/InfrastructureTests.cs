using Subverted.App.Infrastructure;
using Subverted.App.ViewModels;
using Subverted.Frontend;

namespace Subverted.App.Tests;

/// <summary>The adapters, against the real filesystem and a daemon that is not there.</summary>
public sealed class InfrastructureTests
{
    [Test]
    public async Task The_recent_list_survives_a_restart()
    {
        using var folder = new ScratchFolder();
        var path = Path.Combine(folder.Path, "nested", "recent.json");

        new RecentWorkingCopiesFile(path).Save(["/game", "/tools"]);
        var loaded = new RecentWorkingCopiesFile(path).Load();

        await Assert.That(string.Join(",", loaded)).IsEqualTo("/game,/tools");
        await Assert.That(File.Exists(path + ".new")).IsFalse();
    }

    [Test]
    public async Task No_file_yet_is_an_empty_list()
    {
        using var folder = new ScratchFolder();

        await Assert
            .That(new RecentWorkingCopiesFile(Path.Combine(folder.Path, "none.json")).Load())
            .IsEmpty();
    }

    /// <summary>A half-written or hand-edited file loses the sidebar's list, not the app's start.</summary>
    [Test]
    public async Task An_unreadable_file_is_an_empty_list()
    {
        using var folder = new ScratchFolder();
        var path = Path.Combine(folder.Path, "recent.json");
        File.WriteAllText(path, "{ not json");

        await Assert.That(new RecentWorkingCopiesFile(path).Load()).IsEmpty();
    }

    /// <summary>
    /// Nothing listening and nothing to start: the view must hear "unreachable", whatever the
    /// channel happened to throw on the way.
    /// </summary>
    [Test]
    public async Task A_daemon_that_is_not_there_reads_as_unreachable()
    {
        using var folder = new ScratchFolder();
        var status = new DaemonWorkingCopyStatus(
            new DaemonChannel(
                Path.Combine(folder.Path, "nobody.sock"),
                Path.Combine(folder.Path, "no-daemon.exe")
            )
        );

        var thrown = await Assert
            .That(async () => await status.ReadAsync(folder.Path, CancellationToken.None))
            .Throws<DaemonUnreachableException>();

        await Assert.That(thrown!.Message).Contains("no-daemon.exe");
    }

    private sealed class ScratchFolder : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"subverted-app-{Guid.NewGuid():N}"
            );

        public ScratchFolder() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
