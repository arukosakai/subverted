using System.ComponentModel;
using System.Diagnostics;
using System.Net.Sockets;
using Subverted.Protocol;

namespace Subverted.Frontend;

/// <summary>
/// Talks to the daemon, starting one when there is none. A front-end that makes people launch a
/// background process by hand is one they work around by not using it.
/// </summary>
/// <param name="daemonPath">The daemon executable. See <see cref="ExecutableNextTo"/>.</param>
public sealed class DaemonChannel(string socketPath, string daemonPath)
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BetweenAttempts = TimeSpan.FromMilliseconds(25);

    /// <summary>Both executables come out of one publish directory, so <c>sv</c> knows where the daemon is.</summary>
    public static string ExecutableNextTo(string directory) =>
        ExecutableNextTo(directory, OperatingSystem.IsWindows());

    /// <param name="isWindows">Taken as an argument so both answers are tested on either platform.</param>
    internal static string ExecutableNextTo(string directory, bool isWindows) =>
        Path.Combine(directory, isWindows ? "subverted-daemon.exe" : "subverted-daemon");

    /// <exception cref="TimeoutException">A daemon was started and never began listening.</exception>
    /// <exception cref="IOException">No daemon was listening and none could be started.</exception>
    public async Task<DaemonResponse> SendAsync(
        DaemonRequest request,
        CancellationToken cancellationToken
    )
    {
        await using var client = await ConnectOrStartAsync(cancellationToken);
        return await client.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// For requests that must not bring a daemon into being to answer them — stopping one that is
    /// already gone is a success, not a reason to start it.
    /// </summary>
    /// <returns><c>null</c> when no daemon is listening.</returns>
    public async Task<DaemonResponse?> SendIfRunningAsync(
        DaemonRequest request,
        CancellationToken cancellationToken
    )
    {
        DaemonClient client;
        try
        {
            client = await DaemonClient.ConnectAsync(socketPath, cancellationToken);
        }
        catch (SocketException)
        {
            return null;
        }

        await using (client)
        {
            return await client.SendAsync(request, cancellationToken);
        }
    }

    private async Task<DaemonClient> ConnectOrStartAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await DaemonClient.ConnectAsync(socketPath, cancellationToken);
        }
        catch (SocketException)
        {
            Start();
        }

        // Two front-ends can race to start a daemon; the one that loses the bind exits and the
        // other one answers both, so retrying the connect is the whole of the resolution.
        var deadline = DateTime.UtcNow + StartupTimeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                return await DaemonClient.ConnectAsync(socketPath, cancellationToken);
            }
            catch (SocketException)
            {
                await Task.Delay(BetweenAttempts, cancellationToken);
            }
        }

        throw new TimeoutException(
            $"Started '{daemonPath}' but nothing is listening on '{socketPath}'."
        );
    }

    /// <exception cref="FileNotFoundException">The daemon is not installed next to this binary.</exception>
    /// <exception cref="IOException">It is there but the system would not run it.</exception>
    private void Start()
    {
        if (!File.Exists(daemonPath))
        {
            throw new FileNotFoundException(
                $"No daemon to start: '{daemonPath}' is not there.",
                daemonPath
            );
        }

        // Both halves are needed, and each is useless alone: this stops the daemon receiving a copy
        // of our console, and the redirection below stops it being handed ours as its own.
        StandardHandleInheritance.Disable();

        try
        {
            Spawn();
        }
        catch (Win32Exception exception)
        {
            throw new IOException(
                $"Could not start the daemon '{daemonPath}': {exception.Message}",
                exception
            );
        }
    }

    private void Spawn()
    {
        using var started = Process.Start(
            new ProcessStartInfo(daemonPath, ["--detached"])
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                // The daemon outlives `sv`, so it must not hold anything the shell is waiting on:
                // a pipe it still owns means `sv st | grep x` never sees end-of-file on a command
                // that already finished. These three pipes die with this process.
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // Not the caller's directory: a long-lived process holding it open stops the user
                // from deleting or switching the tree it is sitting in.
                WorkingDirectory = Path.GetTempPath(),
            }
        );
    }
}
