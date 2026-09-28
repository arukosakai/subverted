using System.Text;
using Subverted.Core;

namespace Subverted.Protocol.Tests;

public sealed class IncomingMessageTests
{
    [Test]
    public async Task An_incoming_request_round_trips_its_path()
    {
        var request = new IncomingRequest("/wc/art");

        var json = Encoding.UTF8.GetString(ProtocolMessage.Encode(request));

        await Assert
            .That(ProtocolMessage.DecodeRequest(Encoding.UTF8.GetBytes(json)))
            .IsEqualTo(request);
        await Assert.That(json).Contains("\"$kind\":\"incoming\"");
    }

    /// <summary>
    /// Record equality compares the list by reference, so each field of each change is checked,
    /// the one with no node change among them.
    /// </summary>
    [Test]
    public async Task An_incoming_response_round_trips_every_change_and_the_revision()
    {
        var response = new IncomingResponse(
            new IncomingChanges(
                9,
                [
                    new IncomingChange("art/hero.png", PathChange.Replaced, false),
                    new IncomingChange("art", null, PropertiesChanged: true),
                ]
            )
        );

        var json = Encoding.UTF8.GetString(ProtocolMessage.Encode(response));
        var decoded = (IncomingResponse)
            ProtocolMessage.DecodeResponse(ProtocolMessage.Encode(response));

        await Assert.That(json).Contains("\"$kind\":\"incoming\"");
        await Assert.That(decoded.Incoming.AgainstRevision).IsEqualTo(9L);
        await Assert.That(decoded.Incoming.Changes).IsEquivalentTo(response.Incoming.Changes);
    }
}
