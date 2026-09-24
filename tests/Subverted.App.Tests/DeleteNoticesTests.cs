using Subverted.App.ViewModels;
using Subverted.Protocol;

namespace Subverted.App.Tests;

public sealed class DeleteNoticesTests
{
    private const string PartWay =
        "Some of it may already be deleted; the list shows what is left.";

    [Test]
    public async Task A_delete_that_went_through_says_how_to_record_or_undo_it()
    {
        await Assert
            .That(DeleteNotices.For("art/a.png", new DeleteResponse("D  art/a.png")))
            .IsEqualTo(
                new Notice(
                    NoticeKind.Succeeded,
                    "Deleted art/a.png",
                    null,
                    "Commit to record it. Until then, Revert brings back what SVN had."
                )
            );
    }

    [Test]
    public async Task A_refused_delete_is_uncertain_and_carries_svn_s_text()
    {
        var refused = new ErrorResponse(DaemonErrorKind.SvnCommandFailed, "svn: E155004");

        await Assert
            .That(DeleteNotices.For("art", refused))
            .IsEqualTo(
                new Notice(NoticeKind.Uncertain, "art was not deleted", "svn: E155004", PartWay)
            );
    }

    [Test]
    public async Task An_answer_that_is_not_a_delete_is_uncertain_and_named()
    {
        await Assert
            .That(DeleteNotices.For("art", new RevertResponse("")))
            .IsEqualTo(
                new Notice(
                    NoticeKind.Uncertain,
                    "The delete's answer was not understood",
                    "The daemon answered with RevertResponse, which is not a delete.",
                    PartWay
                )
            );
    }

    [Test]
    public async Task A_target_gone_before_the_delete_wrote_nothing()
    {
        await Assert
            .That(DeleteNotices.NothingLeft("art/a.png"))
            .IsEqualTo(
                new Notice(
                    NoticeKind.NothingWritten,
                    "Nothing left to delete at art/a.png",
                    null,
                    null
                )
            );
    }

    [Test]
    public async Task A_refusal_before_sending_wrote_nothing_and_says_why()
    {
        await Assert
            .That(DeleteNotices.Refused("a.png", "a.png is already marked deleted."))
            .IsEqualTo(
                new Notice(
                    NoticeKind.NothingWritten,
                    "a.png was not deleted",
                    "a.png is already marked deleted.",
                    "Nothing was written."
                )
            );
    }

    [Test]
    public async Task A_listing_that_could_not_be_read_wrote_nothing()
    {
        await Assert
            .That(DeleteNotices.CouldNotLook("art", "connection refused"))
            .IsEqualTo(
                new Notice(
                    NoticeKind.NothingWritten,
                    "Could not see what deleting art would take",
                    "connection refused",
                    "Nothing was deleted."
                )
            );
    }

    [Test]
    public async Task A_daemon_gone_during_the_delete_is_uncertain()
    {
        await Assert
            .That(DeleteNotices.Unreachable("gone"))
            .IsEqualTo(
                new Notice(NoticeKind.Uncertain, "The daemon is not answering", "gone", PartWay)
            );
    }
}
