using Subverted.App.ViewModels;
using Subverted.Protocol;

namespace Subverted.App.Tests;

/// <summary>A failed revert may have reverted part of its target, so it is never "nothing happened".</summary>
public sealed class RevertNoticesTests
{
    private const string PartWay =
        "Some of it may already be reverted; the list shows what is left.";

    [Test]
    public async Task A_revert_that_ran_names_its_target()
    {
        await Assert
            .That(RevertNotices.For("art/a.png", new RevertResponse("Reverted 'art\\a.png'")))
            .IsEqualTo(new Notice(NoticeKind.Succeeded, "Reverted art/a.png", null, null));
    }

    [Test]
    public async Task A_refused_revert_shows_the_daemon_s_text_and_may_have_got_part_way()
    {
        var notice = RevertNotices.For(
            "art",
            new ErrorResponse(DaemonErrorKind.SvnCommandFailed, "svn: E155004: locked")
        );

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(
                    NoticeKind.Uncertain,
                    "art was not reverted",
                    "svn: E155004: locked",
                    PartWay
                )
            );
    }

    [Test]
    public async Task An_answer_that_is_not_a_revert_is_uncertain()
    {
        var notice = RevertNotices.For("a.png", new DiffResponse(""));

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(
                    NoticeKind.Uncertain,
                    "The revert's answer was not understood",
                    "The daemon answered with DiffResponse, which is not a revert.",
                    PartWay
                )
            );
    }

    [Test]
    public async Task An_unreachable_daemon_is_uncertain()
    {
        await Assert
            .That(RevertNotices.Unreachable("gone"))
            .IsEqualTo(
                new Notice(NoticeKind.Uncertain, "The daemon is not answering", "gone", PartWay)
            );
    }
}
