using Subverted.App.ViewModels;
using Subverted.Protocol;

namespace Subverted.App.Tests;

/// <summary>An update that left a conflict finished, but it never reads as a clean one.</summary>
public sealed class UpdateNoticesTests
{
    private const string PartWay =
        "Some of it may already have come down; the list shows where things stand.";

    private const string ConflictsHint =
        "The files are on disk with SVN's markers beside them, pinned at the top of the list.";

    private const string SkippedHint =
        "Nothing was updated where SVN skipped; its text names them.";

    [Test]
    public async Task A_clean_update_names_the_revision_and_shows_svn_s_text()
    {
        var notice = UpdateNotices.For(
            new UpdateResponse(42, 0, 0, "U    art/hero.png\nUpdated to revision 42.\n")
        );

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(
                    NoticeKind.Succeeded,
                    "Updated to r42",
                    "U    art/hero.png\nUpdated to revision 42.",
                    null
                )
            );
    }

    [Test]
    public async Task An_update_svn_gave_no_revision_for_says_only_that_it_updated()
    {
        var notice = UpdateNotices.For(new UpdateResponse(null, 0, 0, "text"));

        await Assert.That(notice.Headline).IsEqualTo("Updated");
    }

    [Test]
    [Arguments("")]
    [Arguments("  \n")]
    public async Task An_update_with_no_text_has_no_detail(string notifications)
    {
        var notice = UpdateNotices.For(new UpdateResponse(7, 0, 0, notifications));

        await Assert.That(notice.Detail).IsNull();
    }

    [Test]
    public async Task A_revision_is_written_without_a_thousands_separator()
    {
        var notice = UpdateNotices.For(new UpdateResponse(12345, 0, 0, ""));

        await Assert.That(notice.Headline).IsEqualTo("Updated to r12345");
    }

    [Test]
    [Arguments(1, "Updated to r9 · 1 conflict to resolve")]
    [Arguments(2, "Updated to r9 · 2 conflicts to resolve")]
    [Arguments(1500, "Updated to r9 · 1,500 conflicts to resolve")]
    public async Task Conflicts_need_the_person_and_say_how_many(int conflicts, string headline)
    {
        var notice = UpdateNotices.For(new UpdateResponse(9, conflicts, 0, "C    a.png"));

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(NoticeKind.NeedsAttention, headline, "C    a.png", ConflictsHint)
            );
    }

    [Test]
    [Arguments(1, "Updated to r9 · 1 path skipped")]
    [Arguments(3, "Updated to r9 · 3 paths skipped")]
    public async Task Skipped_paths_need_the_person_and_say_how_many(int skipped, string headline)
    {
        var notice = UpdateNotices.For(new UpdateResponse(9, 0, skipped, "Skipped 'x'"));

        await Assert
            .That(notice)
            .IsEqualTo(new Notice(NoticeKind.NeedsAttention, headline, "Skipped 'x'", SkippedHint));
    }

    [Test]
    public async Task Conflicts_and_skipped_paths_are_both_said_conflicts_first()
    {
        var notice = UpdateNotices.For(new UpdateResponse(9, 2, 1, "text"));

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(
                    NoticeKind.NeedsAttention,
                    "Updated to r9 · 2 conflicts to resolve · 1 path skipped",
                    "text",
                    ConflictsHint + " " + SkippedHint
                )
            );
    }

    [Test]
    public async Task A_refused_update_shows_the_daemon_s_text_and_may_have_got_part_way()
    {
        var notice = UpdateNotices.For(
            new ErrorResponse(DaemonErrorKind.SvnCommandFailed, "svn: E155004: locked")
        );

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(
                    NoticeKind.Uncertain,
                    "The update did not finish",
                    "svn: E155004: locked",
                    PartWay
                )
            );
    }

    [Test]
    public async Task An_answer_that_is_not_an_update_is_uncertain()
    {
        await Assert
            .That(UpdateNotices.For(new DiffResponse("")))
            .IsEqualTo(
                new Notice(
                    NoticeKind.Uncertain,
                    "The update's answer was not understood",
                    "The daemon answered with DiffResponse, which is not an update.",
                    PartWay
                )
            );
    }

    [Test]
    public async Task An_unreachable_daemon_is_uncertain()
    {
        await Assert
            .That(UpdateNotices.Unreachable("gone"))
            .IsEqualTo(
                new Notice(NoticeKind.Uncertain, "The daemon is not answering", "gone", PartWay)
            );
    }
}
