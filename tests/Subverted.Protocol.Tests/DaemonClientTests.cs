using System.Net.Sockets;

namespace Subverted.Protocol.Tests;

/// <summary>
/// Against a real <c>AF_UNIX</c> socket rather than a stream pair, because "does this work on
/// Windows at all" is the whole question D4 answers.
/// </summary>
public sealed class DaemonClientTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Test]
    public async Task A_request_reaches_the_listener_and_its_answer_comes_back()
    {
        await WithListener(
            async accepted =>
            {
                await using var connection = Server(accepted);
                var request = (StatusRequest)(await connection.ReceiveRequestAsync(None))!;
                await connection.SendAsync(
                    new ErrorResponse(DaemonErrorKind.NotAWorkingCopy, request.WorkingCopyPath),
                    None
                );
            },
            async socketPath =>
            {
                await using var client = await DaemonClient.ConnectAsync(socketPath, None);

                var response = await client.SendAsync(
                    new StatusRequest("/wc/art", false, true),
                    None
                );

                await Assert.That(response).IsTypeOf<ErrorResponse>();
                await Assert.That(((ErrorResponse)response).Message).IsEqualTo("/wc/art");
            }
        );
    }

    /// <summary>
    /// A daemon killed mid-request must not leave the caller waiting forever for a frame that is
    /// never coming.
    /// </summary>
    [Test]
    public async Task A_daemon_that_hangs_up_without_answering_is_a_protocol_error()
    {
        await WithListener(
            async accepted =>
            {
                await using var connection = Server(accepted);
                await connection.ReceiveRequestAsync(None);
            },
            async socketPath =>
            {
                await using var client = await DaemonClient.ConnectAsync(socketPath, None);

                await Assert
                    .That(async () => await client.SendAsync(new ShutdownRequest(), None))
                    .Throws<ProtocolException>();
            }
        );
    }

    [Test]
    public async Task Connecting_where_nothing_listens_fails_instead_of_waiting()
    {
        var socketPath = Path.Combine(Path.GetTempPath(), $"sv-{Guid.NewGuid():N}"[..12] + ".sock");

        await Assert
            .That(async () => await DaemonClient.ConnectAsync(socketPath, None))
            .Throws<SocketException>();
    }

    private static DaemonConnection Server(Socket accepted) =>
        new(new NetworkStream(accepted, ownsSocket: true), MessageFramer.Default);

    private static async Task WithListener(Func<Socket, Task> serve, Func<string, Task> useClient)
    {
        var socketPath = Path.Combine(Path.GetTempPath(), $"sv-{Guid.NewGuid():N}"[..12] + ".sock");
        using var listener = new Socket(
            AddressFamily.Unix,
            SocketType.Stream,
            ProtocolType.Unspecified
        );
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(backlog: 1);

        var serving = Task.Run(async () => await serve(await listener.AcceptAsync()));
        try
        {
            await useClient(socketPath);
            await serving;
        }
        finally
        {
            listener.Dispose();
            File.Delete(socketPath);
        }
    }
}
