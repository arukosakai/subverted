using System.Text;
using Subverted.Core;

namespace Subverted.Protocol.Tests;

/// <summary>
/// How much context a diff is asked for and was given. Literal JSON pins what a build from before
/// the field sends, which must read as svn's own three lines, exactly as it did.
/// </summary>
public sealed class DiffContextMessageTests
{
    [Test]
    [Arguments(10)]
    [Arguments(0)]
    public async Task A_diff_request_round_trips_its_lines_of_context(int lines)
    {
        var request = new DiffRequest("/wc/a.txt", new DiffContext(lines));

        await Assert
            .That(ProtocolMessage.DecodeRequest(ProtocolMessage.Encode(request)))
            .IsEqualTo(request);
    }

    [Test]
    public async Task A_diff_request_for_the_whole_file_round_trips_as_the_whole_file()
    {
        var decoded = (DiffRequest)
            ProtocolMessage.DecodeRequest(
                ProtocolMessage.Encode(new DiffRequest("/wc/a.txt", DiffContext.WholeFile))
            );

        await Assert.That(decoded.Context).IsEqualTo(DiffContext.WholeFile);
        await Assert.That(decoded.Context!.LinesAround).IsNull();
    }

    [Test]
    public async Task A_diff_request_from_a_build_that_had_no_context_asks_for_svns_own()
    {
        var decoded = ProtocolMessage.DecodeRequest("""{"$kind":"diff","path":"/wc"}"""u8);

        await Assert.That(decoded).IsEqualTo(new DiffRequest("/wc", Context: null));
    }

    [Test]
    public async Task Svns_own_context_and_the_whole_file_are_different_things_on_the_wire()
    {
        await Assert.That(Json(new DiffRequest("/wc"))).Contains("\"context\":null");
        await Assert
            .That(Json(new DiffRequest("/wc", DiffContext.WholeFile)))
            .Contains("\"context\":{\"linesAround\":null}");
    }

    [Test]
    public async Task A_revision_diff_request_round_trips_its_context()
    {
        var request = new RevisionDiffRequest("/wc", "/trunk/a.txt", 7, new DiffContext(25));

        await Assert
            .That(ProtocolMessage.DecodeRequest(ProtocolMessage.Encode(request)))
            .IsEqualTo(request);
    }

    [Test]
    public async Task A_revision_diff_request_from_a_build_that_had_no_context_asks_for_svns_own()
    {
        var decoded = ProtocolMessage.DecodeRequest(
            """{"$kind":"revision-diff","workingCopyPath":"/wc","repositoryPath":"/a.txt","revision":7}"""u8
        );

        await Assert
            .That(decoded)
            .IsEqualTo(new RevisionDiffRequest("/wc", "/a.txt", 7, Context: null));
    }

    [Test]
    public async Task A_diff_response_round_trips_the_context_it_was_written_with()
    {
        var decoded = (DiffResponse)
            ProtocolMessage.DecodeResponse(
                ProtocolMessage.Encode(new DiffResponse("Index: a\n", new DiffContext(10)))
            );

        await Assert.That(decoded.Context).IsEqualTo(new DiffContext(10));
        await Assert.That(decoded.UnifiedDiff).IsEqualTo("Index: a\n");
    }

    [Test]
    public async Task A_diff_response_from_a_daemon_that_had_no_context_says_nothing_about_it()
    {
        var decoded = (DiffResponse)
            ProtocolMessage.DecodeResponse("""{"$kind":"diff","unifiedDiff":""}"""u8);

        await Assert.That(decoded.Context).IsNull();
    }

    private static string Json(DaemonRequest request) =>
        Encoding.UTF8.GetString(ProtocolMessage.Encode(request));
}
