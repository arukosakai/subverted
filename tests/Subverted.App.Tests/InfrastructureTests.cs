using System.Net.Sockets;
using Subverted.App.Infrastructure;
using Subverted.App.Presentation;
using Subverted.App.ViewModels;
using Subverted.Frontend;
using Subverted.Protocol;

namespace Subverted.App.Tests;

/// <summary>
/// The adapters, against the real filesystem and a daemon that is not there — or, for what a
/// request carries, a bare listener that reads it off the socket.
/// </summary>
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
        await Assert
            .That(Directory.GetFiles(Path.GetDirectoryName(path)!))
            .IsEquivalentTo(new[] { path });
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

    /// <summary>A hand-edited <c>null</c> would otherwise fail every start until the file is deleted.</summary>
    [Test]
    public async Task Blank_and_null_entries_are_dropped_from_the_list()
    {
        using var folder = new ScratchFolder();
        var path = Path.Combine(folder.Path, "recent.json");
        File.WriteAllText(path, """["/game", null, "", "  ", "/tools"]""");

        var loaded = new RecentWorkingCopiesFile(path).Load();

        await Assert.That(string.Join(",", loaded)).IsEqualTo("/game,/tools");
    }

    /// <summary>Another instance holding the file, or a read-only profile, costs the list, not the open.</summary>
    [Test]
    public async Task A_list_that_cannot_be_written_is_not_an_error()
    {
        using var folder = new ScratchFolder();
        var blocker = Path.Combine(folder.Path, "not-a-folder");
        File.WriteAllText(blocker, "");
        var store = new RecentWorkingCopiesFile(Path.Combine(blocker, "recent.json"));

        store.Save(["/game"]);

        await Assert.That(store.Load()).IsEmpty();
    }

    [Test]
    public async Task A_failed_write_leaves_the_kept_list_and_no_staging_file()
    {
        using var folder = new ScratchFolder();
        var path = Path.Combine(folder.Path, "recent.json");
        var store = new RecentWorkingCopiesFile(path);
        store.Save(["/game"]);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            store.Save(["/tools", "/game"]);
        }

        await Assert.That(string.Join(",", store.Load())).IsEqualTo("/game");
        await Assert.That(Directory.GetFiles(folder.Path)).IsEquivalentTo(new[] { path });
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
            new DaemonChannel(UnusedSocket.NewPath(), Path.Combine(folder.Path, "no-daemon.exe"))
        );

        var thrown = await Assert
            .That(async () =>
                await status.ReadAsync(
                    folder.Path,
                    ListedNodes.Changes,
                    heldScan: null,
                    CancellationToken.None
                )
            )
            .Throws<DaemonUnreachableException>();

        await Assert.That(thrown!.Message).Contains("no-daemon.exe");
    }

    /// <summary>
    /// What reaches the daemon, read off a real socket: All is <c>svn status -v</c>'s switch and
    /// nothing else, the listing stays scoped to the opened folder, and the held scan goes with it.
    /// </summary>
    [Test]
    [Arguments(ListedNodes.Changes, false)]
    [Arguments(ListedNodes.All, true)]
    public async Task The_status_asked_of_the_daemon_carries_the_listing_and_the_held_scan(
        ListedNodes listed,
        bool includeUnmodified
    )
    {
        var socketPath = UnusedSocket.NewPath();
        using var listener = new Socket(
            AddressFamily.Unix,
            SocketType.Stream,
            ProtocolType.Unspecified
        );
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(backlog: 1);
        var serving = Task.Run(async () =>
        {
            await using var connection = new DaemonConnection(
                new NetworkStream(await listener.AcceptAsync(), ownsSocket: true),
                MessageFramer.Default
            );
            var request = await connection.ReceiveRequestAsync(CancellationToken.None);
            await connection.SendAsync(
                new ErrorResponse(DaemonErrorKind.Internal, "x"),
                CancellationToken.None
            );
            return (StatusRequest)request!;
        });
        var held = Guid.NewGuid();
        var status = new DaemonWorkingCopyStatus(new DaemonChannel(socketPath, "no-daemon.exe"));

        try
        {
            await status.ReadAsync("/studio/game/art", listed, held, CancellationToken.None);
            var asked = await serving;

            await Assert.That(asked.IncludeUnmodified).IsEqualTo(includeUnmodified);
            await Assert.That(asked.IncludeIgnored).IsFalse();
            await Assert.That(asked.Scope).IsEquivalentTo(new[] { "/studio/game/art" });
            await Assert.That(asked.HeldScan).IsEqualTo(held);
        }
        finally
        {
            listener.Dispose();
            File.Delete(socketPath);
        }
    }

    [Test]
    public async Task A_diff_asked_of_a_daemon_that_is_not_there_reads_as_unreachable()
    {
        using var folder = new ScratchFolder();
        var diffs = new DaemonWorkingCopyDiff(
            new DaemonChannel(UnusedSocket.NewPath(), Path.Combine(folder.Path, "no-daemon.exe"))
        );

        var thrown = await Assert
            .That(async () => await diffs.ReadAsync(folder.Path, CancellationToken.None))
            .Throws<DaemonUnreachableException>();

        await Assert.That(thrown!.Message).Contains("no-daemon.exe");
    }

    [Test]
    public async Task A_commit_sent_to_a_daemon_that_is_not_there_reads_as_unreachable()
    {
        using var folder = new ScratchFolder();
        var commits = new DaemonWorkingCopyCommit(
            new DaemonChannel(UnusedSocket.NewPath(), Path.Combine(folder.Path, "no-daemon.exe"))
        );

        var thrown = await Assert
            .That(async () =>
                await commits.CommitAsync([folder.Path], "message", CancellationToken.None)
            )
            .Throws<DaemonUnreachableException>();

        await Assert.That(thrown!.Message).Contains("no-daemon.exe");
    }

    [Test]
    public async Task A_revert_sent_to_a_daemon_that_is_not_there_reads_as_unreachable()
    {
        using var folder = new ScratchFolder();
        var reverts = new DaemonWorkingCopyRevert(
            new DaemonChannel(UnusedSocket.NewPath(), Path.Combine(folder.Path, "no-daemon.exe"))
        );

        var thrown = await Assert
            .That(async () => await reverts.RevertAsync(folder.Path, CancellationToken.None))
            .Throws<DaemonUnreachableException>();

        await Assert.That(thrown!.Message).Contains("no-daemon.exe");
    }

    [Test]
    public async Task An_update_sent_to_a_daemon_that_is_not_there_reads_as_unreachable()
    {
        using var folder = new ScratchFolder();
        var updates = new DaemonWorkingCopyUpdate(
            new DaemonChannel(UnusedSocket.NewPath(), Path.Combine(folder.Path, "no-daemon.exe"))
        );

        var thrown = await Assert
            .That(async () => await updates.UpdateAsync(folder.Path, CancellationToken.None))
            .Throws<DaemonUnreachableException>();

        await Assert.That(thrown!.Message).Contains("no-daemon.exe");
    }

    [Test]
    public async Task A_lock_sent_to_a_daemon_that_is_not_there_reads_as_unreachable()
    {
        using var folder = new ScratchFolder();
        var locks = new DaemonWorkingCopyLocks(
            new DaemonChannel(UnusedSocket.NewPath(), Path.Combine(folder.Path, "no-daemon.exe"))
        );

        var thrown = await Assert
            .That(async () => await locks.LockAsync(folder.Path, CancellationToken.None))
            .Throws<DaemonUnreachableException>();

        await Assert.That(thrown!.Message).Contains("no-daemon.exe");
    }

    [Test]
    public async Task An_unlock_sent_to_a_daemon_that_is_not_there_reads_as_unreachable()
    {
        using var folder = new ScratchFolder();
        var locks = new DaemonWorkingCopyLocks(
            new DaemonChannel(UnusedSocket.NewPath(), Path.Combine(folder.Path, "no-daemon.exe"))
        );

        var thrown = await Assert
            .That(async () => await locks.UnlockAsync(folder.Path, CancellationToken.None))
            .Throws<DaemonUnreachableException>();

        await Assert.That(thrown!.Message).Contains("no-daemon.exe");
    }

    [Test]
    public async Task A_files_size_is_its_length_on_disk()
    {
        using var folder = new ScratchFolder();
        var path = Path.Combine(folder.Path, "hero.png");
        File.WriteAllBytes(path, new byte[1234]);

        await Assert.That(new FileSizeReader().ReadSize(path)).IsEqualTo(1234);
    }

    [Test]
    public async Task A_missing_file_or_a_folder_has_no_size()
    {
        using var folder = new ScratchFolder();

        await Assert
            .That(new FileSizeReader().ReadSize(Path.Combine(folder.Path, "gone.png")))
            .IsNull();
        await Assert.That(new FileSizeReader().ReadSize(folder.Path)).IsNull();
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
