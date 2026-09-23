using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Subverted.Protocol;

namespace Subverted.Daemon;

/// <summary>
/// Listens on the daemon's Unix domain socket and serves whatever connects to it. One connection
/// may carry any number of requests; a front-end hanging up is the ordinary end of one.
/// </summary>
public sealed class DaemonSocketServer(
    string socketPath,
    DaemonRequestHandler handler,
    ILogger<DaemonSocketServer> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(socketPath)!);
        RemoveStaleSocket();

        using var listener = new Socket(
            AddressFamily.Unix,
            SocketType.Stream,
            ProtocolType.Unspecified
        );
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(backlog: 64);
        logger.LogInformation("Listening on {SocketPath}.", socketPath);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var accepted = await listener.AcceptAsync(stoppingToken);
                _ = ServeAsync(accepted, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
        finally
        {
            // Closed before the file is removed, and before `using` would have got to it: deleting
            // the path out from under a live listener leaves nothing for the next daemon to find
            // as stale, and nothing for this one to have been listening on.
            listener.Dispose();
            File.Delete(socketPath);
        }
    }

    /// <summary>
    /// A daemon that was killed leaves its socket file behind and the next bind fails on it. A file
    /// nothing answers on is stale; one that accepts a connection means a daemon is already up, and
    /// starting a second would leave front-ends talking to whichever bound last.
    /// </summary>
    /// <exception cref="IOException">Another daemon is already listening there.</exception>
    private void RemoveStaleSocket()
    {
        if (!File.Exists(socketPath))
        {
            return;
        }

        using var probe = new Socket(
            AddressFamily.Unix,
            SocketType.Stream,
            ProtocolType.Unspecified
        );
        try
        {
            probe.Connect(new UnixDomainSocketEndPoint(socketPath));
        }
        catch (SocketException)
        {
            logger.LogInformation("Removing a stale socket at {SocketPath}.", socketPath);
            File.Delete(socketPath);
            return;
        }

        throw new IOException($"Another daemon is already listening on '{socketPath}'.");
    }

    private async Task ServeAsync(Socket accepted, CancellationToken cancellationToken)
    {
        await using var connection = new DaemonConnection(
            new NetworkStream(accepted, ownsSocket: true),
            MessageFramer.Default
        );

        try
        {
            while (await connection.ReceiveRequestAsync(cancellationToken) is { } request)
            {
                var response = await AnswerAsync(request, cancellationToken);
                await connection.SendAsync(response, cancellationToken);
            }
        }
        // Nothing thrown while serving one connection may reach the accept loop: this runs
        // detached from it, so an escaping exception is an unobserved task rather than a failure
        // anyone sees. The handler's own failures are already answered in AnswerAsync.
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogDebug(ex, "Dropped a connection.");
        }
    }

    /// <summary>
    /// A handler that throws answers with an error rather than dropping the connection: a
    /// front-end can print a message, and cannot do anything sensible with a hang-up.
    /// </summary>
    private async Task<DaemonResponse> AnswerAsync(
        DaemonRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await handler.HandleAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "{Request} failed.", request.GetType().Name);
            return new ErrorResponse(DaemonErrorKind.Internal, ex.Message);
        }
    }
}
