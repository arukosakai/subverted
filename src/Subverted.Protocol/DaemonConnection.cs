namespace Subverted.Protocol;

/// <summary>
/// One framed, typed message channel over a duplex stream. Both ends use it: the daemon receives
/// requests and sends responses, a front-end does the reverse.
/// </summary>
/// <remarks>Disposing the connection disposes the stream it was given.</remarks>
public sealed class DaemonConnection(Stream stream, MessageFramer framer) : IAsyncDisposable
{
    public ValueTask SendAsync(DaemonRequest request, CancellationToken cancellationToken) =>
        framer.WriteAsync(stream, ProtocolMessage.Encode(request), cancellationToken);

    public ValueTask SendAsync(DaemonResponse response, CancellationToken cancellationToken) =>
        framer.WriteAsync(stream, ProtocolMessage.Encode(response), cancellationToken);

    /// <returns><c>null</c> when the peer closed the connection cleanly.</returns>
    public async ValueTask<DaemonRequest?> ReceiveRequestAsync(CancellationToken cancellationToken)
    {
        var payload = await framer.ReadAsync(stream, cancellationToken);
        return payload is null ? null : ProtocolMessage.DecodeRequest(payload);
    }

    /// <returns><c>null</c> when the peer closed the connection cleanly.</returns>
    public async ValueTask<DaemonResponse?> ReceiveResponseAsync(
        CancellationToken cancellationToken
    )
    {
        var payload = await framer.ReadAsync(stream, cancellationToken);
        return payload is null ? null : ProtocolMessage.DecodeResponse(payload);
    }

    public ValueTask DisposeAsync() => stream.DisposeAsync();
}
