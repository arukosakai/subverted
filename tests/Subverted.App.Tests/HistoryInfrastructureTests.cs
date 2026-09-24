using Subverted.App.Infrastructure;
using Subverted.App.ViewModels;
using Subverted.Core;
using Subverted.Frontend;

namespace Subverted.App.Tests;

/// <summary>The History adapters against a daemon that is not there and cannot be started.</summary>
public sealed class HistoryInfrastructureTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Test]
    public async Task A_page_of_history_asked_of_no_daemon_reads_as_unreachable()
    {
        using var folder = new ScratchFolder();
        var history = new DaemonRevisionHistory(Nobody(folder));

        var thrown = await Assert
            .That(async () => await history.ReadAsync(folder.Path, new HistoryFromHead(), 50, None))
            .Throws<DaemonUnreachableException>();

        await Assert.That(thrown!.Message).Contains("no-daemon.exe");
    }

    [Test]
    public async Task A_base_range_asked_of_no_daemon_reads_as_unreachable()
    {
        using var folder = new ScratchFolder();
        var history = new DaemonRevisionHistory(Nobody(folder));

        var thrown = await Assert
            .That(async () => await history.ReadBaseRangeAsync(folder.Path, None))
            .Throws<DaemonUnreachableException>();

        await Assert.That(thrown!.Message).Contains("no-daemon.exe");
    }

    [Test]
    public async Task A_revision_diff_asked_of_no_daemon_reads_as_unreachable()
    {
        using var folder = new ScratchFolder();
        var diffs = new DaemonRevisionDiff(Nobody(folder));

        var thrown = await Assert
            .That(async () => await diffs.ReadAsync(folder.Path, "/a.txt", 2, null, None))
            .Throws<DaemonUnreachableException>();

        await Assert.That(thrown!.Message).Contains("no-daemon.exe");
    }

    private static DaemonChannel Nobody(ScratchFolder folder) =>
        new(UnusedSocket.NewPath(), Path.Combine(folder.Path, "no-daemon.exe"));

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
