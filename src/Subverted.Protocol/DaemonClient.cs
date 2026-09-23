using System.Net.Sockets;

namespace Subverted.Protocol;

/// <summary>
/// A front-end's connection to the daemon: one request, one response, then dispose. Unix domain
/// sockets are supported on Windows 10+ as well, so this is one implementation everywhere (D4).
/// </summary>
public sealed class DaemonClient : IAsyncDisposable
{
    private readonly DaemonConnection _connection;

    private DaemonClient(DaemonConnection connection) => _connection = connection;

    /// <exception cref="SocketException">Nothing is listening — usually "the daemon is not running".</exception>
    public static async Task<DaemonClient> ConnectAsync(
        string socketPath,
        CancellationToken cancellationToken
    )
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return new DaemonClient(
            new DaemonConnection(new NetworkStream(socket, ownsSocket: true), MessageFramer.Default)
        );
    }

    /// <exception cref="ProtocolException">The daemon closed the connection without answering.</exception>
    public async Task<DaemonResponse> SendAsync(
        DaemonRequest request,
        CancellationToken cancellationToken
    )
    {
        await _connection.SendAsync(request, cancellationToken);
        return await _connection.ReceiveResponseAsync(cancellationToken)
            ?? throw new ProtocolException("The daemon closed the connection without answering.");
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
