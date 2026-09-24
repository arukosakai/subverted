using System.Diagnostics;
using Subverted.Frontend;
using Subverted.Protocol;
using static Subverted.Daemon.Tests.DaemonEndToEndTests;

namespace Subverted.Daemon.Tests;

/// <summary>
/// The built daemon, started by <see cref="DaemonChannel"/> exactly as <c>sv</c> and the app start
/// it. What only this can show is what the daemon does to its own process at startup: it is handed
/// a fresh windowless console, not this test's, and svn writes in whatever code page that has.
/// </summary>
public sealed class LaunchedDaemonTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Test]
    [NotInParallel(nameof(DaemonSocketPath.OverrideVariable))]
    [Arguments("names/zażółć.txt")]
    [Arguments("names/ドラゴン.txt")]
    public async Task A_launched_daemon_diffs_a_name_outside_ascii_as_itself(string name)
    {
        using var copy = Committed((name, "one\n"));
        copy.Write(name, "two\n");

        var response = await WithLaunchedDaemon(channel =>
            channel.SendAsync(new DiffRequest(copy.Root), None)
        );

        await Assert.That(response).IsTypeOf<DiffResponse>();
        var diff = ((DiffResponse)response).UnifiedDiff;
        await Assert.That(diff).Contains($"Index: {name}");
        await Assert.That(diff).Contains($"--- {name}\t(revision 1)");
        await Assert.That(diff).Contains($"+++ {name}\t(working copy)");
    }

    /// <remarks>
    /// The daemon takes its socket from the environment it inherits, so the override is set for
    /// the length of the test, which is why the test is not run beside another that sets it.
    /// </remarks>
    private static async Task<DaemonResponse> WithLaunchedDaemon(
        Func<DaemonChannel, Task<DaemonResponse>> use
    )
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sv-{Guid.NewGuid():N}"[..12]);
        Directory.CreateDirectory(directory);
        var socketPath = Path.Combine(directory, "daemon.sock");
        var daemonPath = DaemonChannel.ExecutableNextTo(AppContext.BaseDirectory);
        var channel = new DaemonChannel(socketPath, daemonPath);

        Environment.SetEnvironmentVariable(DaemonSocketPath.OverrideVariable, socketPath);
        try
        {
            return await use(channel);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DaemonSocketPath.OverrideVariable, null);
            await channel.SendIfRunningAsync(new ShutdownRequest(), None);
            await DeleteOnceStoppedAsync(directory, socketPath);
            await ExitedAsync(daemonPath);
        }
    }

    /// <summary>
    /// The socket goes before the process does, and until it has exited its SQLite connection keeps
    /// the working copy's wc.db open, so on Windows the copy could not yet be deleted.
    /// </summary>
    private static async Task ExitedAsync(string daemonPath)
    {
        foreach (
            var daemon in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(daemonPath))
        )
        {
            using var _ = daemon;
            if (IsRunning(daemon, daemonPath))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await daemon.WaitForExitAsync(timeout.Token);
            }
        }
    }

    /// <summary>A daemon someone runs from elsewhere is theirs, and is left alone.</summary>
    private static bool IsRunning(Process process, string path)
    {
        try
        {
            return string.Equals(
                process.MainModule?.FileName,
                path,
                StringComparison.OrdinalIgnoreCase
            );
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// The daemon answers the shutdown before it stops and appends to its log until then, so the
    /// directory waits for the socket, which goes as the server stops.
    /// </summary>
    private static async Task DeleteOnceStoppedAsync(string directory, string socketPath)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (File.Exists(socketPath) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, None);
        }

        Directory.Delete(directory, recursive: true);
    }
}
