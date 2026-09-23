using System.Text;
using Subverted.Core;

namespace Subverted.Protocol.Tests;

/// <summary>
/// The messages the History view adds: where a log starts, one revision's diff, and the BASE range
/// the marker is drawn from. Literal JSON pins what a daemon from the previous build still sends.
/// </summary>
public sealed class HistoryMessageTests
{
    [Test]
    public async Task A_log_request_from_a_build_that_had_no_start_reads_as_starting_at_base()
    {
        var decoded = ProtocolMessage.DecodeRequest(
            """{"$kind":"log","path":"/wc","limit":20}"""u8
        );

        await Assert.That(decoded).IsEqualTo(new LogRequest("/wc", 20, Start: null));
    }

    [Test]
    public async Task A_log_request_starting_at_head_round_trips()
    {
        var request = new LogRequest("/wc", 100, new HistoryFromHead());

        await Assert
            .That(ProtocolMessage.DecodeRequest(ProtocolMessage.Encode(request)))
            .IsEqualTo(request);
    }

    [Test]
    public async Task A_log_request_starting_at_a_revision_round_trips_that_revision()
    {
        var request = new LogRequest("/wc", 100, new HistoryFromRevision(41));

        await Assert
            .That(ProtocolMessage.DecodeRequest(ProtocolMessage.Encode(request)))
            .IsEqualTo(request);
    }

    [Test]
    public async Task Where_a_log_starts_is_named_on_the_wire()
    {
        await Assert
            .That(Json(new LogRequest("/wc", 1, new HistoryFromHead())))
            .Contains("\"start\":{\"$kind\":\"head\"}");
        await Assert
            .That(Json(new LogRequest("/wc", 1, new HistoryFromRevision(41))))
            .Contains("\"start\":{\"$kind\":\"revision\",\"revision\":41}");
    }

    [Test]
    public async Task A_revision_diff_request_round_trips_its_copy_path_and_revision()
    {
        var request = new RevisionDiffRequest("/wc/art", "/trunk/art/hero.png", 1824);

        var json = Json(request);

        await Assert
            .That(ProtocolMessage.DecodeRequest(Encoding.UTF8.GetBytes(json)))
            .IsEqualTo(request);
        await Assert.That(json).Contains("\"$kind\":\"revision-diff\"");
    }

    [Test]
    public async Task A_working_copy_revision_request_round_trips_its_path()
    {
        var request = new WorkingCopyRevisionRequest("/wc/art");

        var json = Json(request);

        await Assert
            .That(ProtocolMessage.DecodeRequest(Encoding.UTF8.GetBytes(json)))
            .IsEqualTo(request);
        await Assert.That(json).Contains("\"$kind\":\"working-copy-revision\"");
    }

    [Test]
    public async Task A_mixed_base_range_round_trips_both_ends()
    {
        var response = new WorkingCopyRevisionResponse(new BaseRevisionRange(3, 5));

        var decoded = ProtocolMessage.DecodeResponse(ProtocolMessage.Encode(response));

        await Assert.That(decoded).IsEqualTo(response);
        await Assert.That(Json(response)).Contains("\"$kind\":\"working-copy-revision\"");
    }

    [Test]
    public async Task A_path_with_no_base_round_trips_as_no_range()
    {
        var decoded = (WorkingCopyRevisionResponse)
            ProtocolMessage.DecodeResponse(
                ProtocolMessage.Encode(new WorkingCopyRevisionResponse(Range: null))
            );

        await Assert.That(decoded.Range).IsNull();
    }

    private static string Json(DaemonRequest request) =>
        Encoding.UTF8.GetString(ProtocolMessage.Encode(request));

    private static string Json(DaemonResponse response) =>
        Encoding.UTF8.GetString(ProtocolMessage.Encode(response));
}
