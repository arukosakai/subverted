using Subverted.App.ViewModels;
using Subverted.Protocol;

namespace Subverted.App.Tests;

public sealed class LockNoticesTests
{
    private const string LookAtTheLine = "The lock icon on its line shows whether you hold it.";

    /// <summary>W160035 as SVN writes it — the only line anywhere that names who holds the file.</summary>
    private const string HeldByRena =
        "svn: warning: W160035: Path '/art/hero.png' is already locked by user 'rena' in filesystem '/repo/db'";

    private const string Gone = "svn: warning: W160040: No lock on path '/art/hero.png'";

    [Test]
    public async Task A_lock_granted_is_named_with_svn_s_own_text()
    {
        var notice = LockNotices.Locked(
            "art/hero.png",
            new LockResponse("'art/hero.png' locked by user 'keiichi'.\r\n", [])
        );

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(
                    NoticeKind.Succeeded,
                    "Locked art/hero.png",
                    "'art/hero.png' locked by user 'keiichi'.",
                    null
                )
            );
    }

    [Test]
    public async Task A_refused_lock_shows_svn_s_warning_naming_the_holder_and_is_never_success()
    {
        var notice = LockNotices.Locked("art/hero.png", new LockResponse("", [HeldByRena]));

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(
                    NoticeKind.NeedsAttention,
                    "art/hero.png was not locked",
                    HeldByRena,
                    "Do not start work on it — SVN's warning says why."
                )
            );
    }

    /// <summary>A refusal outranks whatever else SVN printed, and both are kept, SVN's lines first.</summary>
    [Test]
    public async Task A_refusal_beside_a_granted_lock_still_needs_attention_and_keeps_both_texts()
    {
        var notice = LockNotices.Locked(
            "art/hero.png",
            new LockResponse("'art/a.png' locked by user 'keiichi'.\n", [HeldByRena])
        );

        await Assert.That(notice.Kind).IsEqualTo(NoticeKind.NeedsAttention);
        await Assert
            .That(notice.Detail)
            .IsEqualTo($"'art/a.png' locked by user 'keiichi'.\n{HeldByRena}");
    }

    [Test]
    public async Task A_lock_svn_said_nothing_about_is_not_called_locked()
    {
        var notice = LockNotices.Locked("art/hero.png", new LockResponse(" \n", []));

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(
                    NoticeKind.Uncertain,
                    "SVN said nothing about locking art/hero.png",
                    null,
                    LookAtTheLine
                )
            );
    }

    [Test]
    public async Task A_lock_that_failed_outright_carries_the_daemon_s_message()
    {
        var notice = LockNotices.Locked(
            "art/hero.png",
            new ErrorResponse(
                DaemonErrorKind.SvnCommandFailed,
                "svn: E155010: The node was not found."
            )
        );

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(
                    NoticeKind.Uncertain,
                    "art/hero.png could not be locked",
                    "svn: E155010: The node was not found.",
                    LookAtTheLine
                )
            );
    }

    [Test]
    public async Task An_unlock_answer_to_a_lock_is_not_understood()
    {
        var notice = LockNotices.Locked("a.png", new UnlockResponse("'a.png' unlocked.", []));

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(
                    NoticeKind.Uncertain,
                    "The lock's answer was not understood",
                    "The daemon answered with UnlockResponse, which is not a lock.",
                    LookAtTheLine
                )
            );
    }

    [Test]
    public async Task An_unlock_granted_is_named_with_svn_s_own_text()
    {
        var notice = LockNotices.Unlocked(
            "art/hero.png",
            new UnlockResponse("'art/hero.png' unlocked.\n", [])
        );

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(
                    NoticeKind.Succeeded,
                    "Unlocked art/hero.png",
                    "'art/hero.png' unlocked.",
                    null
                )
            );
    }

    [Test]
    public async Task An_unlock_whose_lock_had_gone_says_so_in_svn_s_words()
    {
        var notice = LockNotices.Unlocked("art/hero.png", new UnlockResponse("", [Gone]));

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(
                    NoticeKind.NeedsAttention,
                    "The lock on art/hero.png had already gone",
                    Gone,
                    "Somebody broke it or took it. This copy does not hold it any more either way."
                )
            );
    }

    [Test]
    public async Task An_unlock_svn_said_nothing_about_is_not_called_unlocked()
    {
        var notice = LockNotices.Unlocked("art/hero.png", new UnlockResponse("", []));

        await Assert.That(notice.Kind).IsEqualTo(NoticeKind.Uncertain);
        await Assert
            .That(notice.Headline)
            .IsEqualTo("SVN said nothing about unlocking art/hero.png");
    }

    [Test]
    public async Task An_unlock_that_failed_outright_carries_the_daemon_s_message()
    {
        var notice = LockNotices.Unlocked(
            "art/hero.png",
            new ErrorResponse(
                DaemonErrorKind.SvnCommandFailed,
                "svn: E195013: not locked in this working copy"
            )
        );

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(
                    NoticeKind.Uncertain,
                    "art/hero.png could not be unlocked",
                    "svn: E195013: not locked in this working copy",
                    LookAtTheLine
                )
            );
    }

    [Test]
    public async Task A_lock_answer_to_an_unlock_is_not_understood()
    {
        var notice = LockNotices.Unlocked("a.png", new LockResponse("'a.png' locked.", []));

        await Assert.That(notice.Headline).IsEqualTo("The unlock's answer was not understood");
        await Assert
            .That(notice.Detail)
            .IsEqualTo("The daemon answered with LockResponse, which is not an unlock.");
    }

    [Test]
    public async Task An_unreachable_daemon_is_uncertain_with_its_message()
    {
        await Assert
            .That(LockNotices.Unreachable("gone"))
            .IsEqualTo(
                new Notice(
                    NoticeKind.Uncertain,
                    "The daemon is not answering",
                    "gone",
                    LookAtTheLine
                )
            );
    }
}
