using System.Text;
using TUnit.Assertions.Enums;

namespace Subverted.Protocol.Tests;

public sealed class CommitSelectionMessageTests
{
    private static readonly SelectionSchedule SampleSchedule = new(
        Added: ["art/new.png", "art/new"],
        Deleted: ["art/old.png"],
        Moved: [new RecordedMove("art/hero.png", "art/protagonist.png")]
    );

    [Test]
    public async Task A_commit_selection_request_round_trips_its_paths_in_order_and_its_message()
    {
        IReadOnlyList<string> paths = ["/wc/art/b.png", "/wc/art/a.png"];
        var message = "re-export \"hero\"\n\nwith the new rig";

        var decoded = (CommitSelectionRequest)
            ProtocolMessage.DecodeRequest(
                ProtocolMessage.Encode(new CommitSelectionRequest(paths, message))
            );

        await Assert.That(decoded.Paths).IsEquivalentTo(paths, CollectionOrdering.Matching);
        await Assert.That(decoded.Message).IsEqualTo(message);
    }

    [Test]
    [Arguments(42)]
    [Arguments(null)]
    public async Task A_committed_selection_round_trips_its_revision_schedule_and_text(
        int? revision
    )
    {
        var decoded = (CommitSelectionResponse)
            ProtocolMessage.DecodeResponse(
                ProtocolMessage.Encode(
                    new CommitSelectionResponse(revision, SampleSchedule, "Adding art/new.png\n")
                )
            );

        await Assert.That(decoded.Revision).IsEqualTo((long?)revision);
        await AssertSampleSchedule(decoded.Scheduled);
        await Assert.That(decoded.Notifications).IsEqualTo("Adding art/new.png\n");
    }

    /// <summary>
    /// The step is what tells a front-end "every mark was made and only sending failed" from "it
    /// stopped half-way through marking", so each value has to survive the wire as itself.
    /// </summary>
    [Test]
    [Arguments(SelectionStep.Move)]
    [Arguments(SelectionStep.Addition)]
    [Arguments(SelectionStep.Deletion)]
    [Arguments(SelectionStep.Commit)]
    public async Task A_selection_not_committed_round_trips_where_it_stopped_and_why(
        SelectionStep step
    )
    {
        var decoded = (SelectionNotCommittedResponse)
            ProtocolMessage.DecodeResponse(
                ProtocolMessage.Encode(
                    new SelectionNotCommittedResponse(
                        SampleSchedule,
                        step,
                        "A  art/new.png\n",
                        "svn: E165001: Commit blocked by pre-commit hook"
                    )
                )
            );

        await Assert.That(decoded.FailedStep).IsEqualTo(step);
        await AssertSampleSchedule(decoded.Scheduled);
        await Assert.That(decoded.Notifications).IsEqualTo("A  art/new.png\n");
        await Assert
            .That(decoded.Failure)
            .IsEqualTo("svn: E165001: Commit blocked by pre-commit hook");
    }

    /// <summary>
    /// Kept apart from <c>commit</c> on purpose: a daemon from before this request existed must
    /// answer it as unknown, not decode it as a plain commit and skip the marking.
    /// </summary>
    [Test]
    public async Task The_selection_kinds_are_named_on_the_wire()
    {
        await Assert
            .That(Json(new CommitSelectionRequest(["/wc"], "m")))
            .Contains("\"$kind\":\"commit-selection\"");
        await Assert
            .That(Json(new CommitSelectionResponse(1, SampleSchedule, "")))
            .Contains("\"$kind\":\"commit-selection\"");
        await Assert
            .That(
                Json(
                    new SelectionNotCommittedResponse(SampleSchedule, SelectionStep.Commit, "", "")
                )
            )
            .Contains("\"$kind\":\"selection-not-committed\"");
    }

    private static async Task AssertSampleSchedule(SelectionSchedule scheduled)
    {
        await Assert
            .That(scheduled.Added)
            .IsEquivalentTo(SampleSchedule.Added, CollectionOrdering.Matching);
        await Assert.That(scheduled.Deleted).IsEquivalentTo(SampleSchedule.Deleted);
        await Assert.That(scheduled.Moved).IsEquivalentTo(SampleSchedule.Moved);
    }

    private static string Json(DaemonRequest request) =>
        Encoding.UTF8.GetString(ProtocolMessage.Encode(request));

    private static string Json(DaemonResponse response) =>
        Encoding.UTF8.GetString(ProtocolMessage.Encode(response));
}
