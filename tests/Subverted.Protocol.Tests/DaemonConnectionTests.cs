namespace Subverted.Protocol.Tests;

public sealed class DaemonConnectionTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Test]
    public async Task A_request_arrives_as_the_request_that_was_sent()
    {
        var wire = new MemoryStream();
        var connection = new DaemonConnection(wire, MessageFramer.Default);

        await connection.SendAsync(new StatusRequest("/wc", true, false), None);
        wire.Position = 0;

        await Assert
            .That(await connection.ReceiveRequestAsync(None))
            .IsEqualTo(new StatusRequest("/wc", true, false));
    }

    [Test]
    public async Task A_response_arrives_as_the_response_that_was_sent()
    {
        var wire = new MemoryStream();
        var connection = new DaemonConnection(wire, MessageFramer.Default);

        await connection.SendAsync(
            new ErrorResponse(DaemonErrorKind.NotAWorkingCopy, "nope"),
            None
        );
        wire.Position = 0;

        await Assert
            .That(await connection.ReceiveResponseAsync(None))
            .IsEqualTo(new ErrorResponse(DaemonErrorKind.NotAWorkingCopy, "nope"));
    }

    /// <summary>
    /// A hung-up peer is null, not an exception: the daemon's accept loop reads until this happens
    /// and a front-end disconnecting is the normal end of a conversation, not an error.
    /// </summary>
    [Test]
    public async Task A_hung_up_peer_reads_as_null_on_both_directions()
    {
        var connection = new DaemonConnection(new MemoryStream(), MessageFramer.Default);

        await Assert.That(await connection.ReceiveRequestAsync(None)).IsNull();
        await Assert.That(await connection.ReceiveResponseAsync(None)).IsNull();
    }

    [Test]
    public async Task Disposing_the_connection_disposes_the_stream_it_was_given()
    {
        var stream = new MemoryStream();

        await new DaemonConnection(stream, MessageFramer.Default).DisposeAsync();

        await Assert.That(stream.CanRead).IsFalse();
    }
}
