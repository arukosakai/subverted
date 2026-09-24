using Subverted.App.ViewModels;
using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.App.Tests;

public sealed class ResolveNoticesTests
{
    private const string PartWay =
        "Some of it may already be resolved; the list shows what is left.";

    private const string TreeConflictHint =
        "A tree conflict can only be marked as resolved, once it is settled by hand.";

    [Test]
    public async Task One_resolved_path_is_named_with_the_version_kept()
    {
        var notice = ResolveNotices.For(
            "art/hero.png",
            ConflictResolution.Mine,
            Resolved("art/hero.png")
        );

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(
                    NoticeKind.Succeeded,
                    "Resolved art/hero.png, keeping your version",
                    null,
                    null
                )
            );
    }

    [Test]
    public async Task Several_resolved_paths_are_counted_and_listed()
    {
        var notice = ResolveNotices.For(
            "art",
            ConflictResolution.Theirs,
            Resolved("art/a.png", "art/b.png")
        );

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(
                    NoticeKind.Succeeded,
                    "Resolved 2 paths in art, keeping the incoming version",
                    "art/a.png\nart/b.png",
                    null
                )
            );
    }

    /// <summary>SVN does not read the file when told to keep it as it is, so the markers may still be there.</summary>
    [Test]
    public async Task Marking_as_resolved_warns_to_look_for_markers()
    {
        var notice = ResolveNotices.For("a.txt", ConflictResolution.Working, Resolved("a.txt"));

        await Assert
            .That(notice.Headline)
            .IsEqualTo("Resolved a.txt, keeping the files as they are on disk");
        await Assert
            .That(notice.Hint)
            .IsEqualTo("Check for <<<<<<< before you commit — nothing looked inside the files.");
    }

    /// <summary><c>svn resolve</c> exits zero having done nothing; that is not "resolved".</summary>
    [Test]
    public async Task Nothing_resolved_and_nothing_refused_says_there_was_no_conflict()
    {
        var notice = ResolveNotices.For(
            "art",
            ConflictResolution.Mine,
            new ResolveResponse([], [])
        );

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(NoticeKind.Succeeded, "Nothing in art was conflicted", null, null)
            );
    }

    [Test]
    public async Task A_refusal_needs_attention_and_shows_svn_s_text()
    {
        var notice = ResolveNotices.For(
            "dir",
            ConflictResolution.Theirs,
            new ResolveResponse([], ["svn: E155027: refused", "svn: E155027: again"])
        );

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(
                    NoticeKind.NeedsAttention,
                    "dir was not resolved",
                    "svn: E155027: refused\nsvn: E155027: again",
                    TreeConflictHint
                )
            );
    }

    [Test]
    public async Task A_refusal_beside_resolved_paths_counts_both()
    {
        var notice = ResolveNotices.For(
            "art",
            ConflictResolution.Mine,
            new ResolveResponse(["art/a.png", "art/b.png"], ["svn: refused"])
        );

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(
                    NoticeKind.NeedsAttention,
                    "Resolved 2 paths; 1 refused",
                    "svn: refused",
                    TreeConflictHint
                )
            );
    }

    /// <summary>The tree-conflict hint points at marking as resolved, which is no help when that is what was refused.</summary>
    [Test]
    public async Task A_refused_mark_as_resolved_does_not_suggest_itself()
    {
        var notice = ResolveNotices.For(
            "a.png",
            ConflictResolution.Working,
            new ResolveResponse([], ["svn: held open"])
        );

        await Assert.That(notice.Kind).IsEqualTo(NoticeKind.NeedsAttention);
        await Assert.That(notice.Hint).IsNull();
    }

    [Test]
    public async Task An_error_is_uncertain_and_may_have_got_part_way()
    {
        var notice = ResolveNotices.For(
            "a.png",
            ConflictResolution.Mine,
            new ErrorResponse(DaemonErrorKind.SvnCommandFailed, "svn: E155004")
        );

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(NoticeKind.Uncertain, "a.png was not resolved", "svn: E155004", PartWay)
            );
    }

    [Test]
    public async Task An_answer_that_is_not_a_resolve_is_uncertain()
    {
        var notice = ResolveNotices.For(
            "a.png",
            ConflictResolution.Mine,
            new AcknowledgedResponse()
        );

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(
                    NoticeKind.Uncertain,
                    "The resolve's answer was not understood",
                    "The daemon answered with AcknowledgedResponse, which is not a resolve.",
                    PartWay
                )
            );
    }

    [Test]
    public async Task An_unreachable_daemon_is_uncertain()
    {
        await Assert
            .That(ResolveNotices.Unreachable("gone"))
            .IsEqualTo(
                new Notice(NoticeKind.Uncertain, "The daemon is not answering", "gone", PartWay)
            );
    }

    private static ResolveResponse Resolved(params string[] paths) => new(paths, []);
}
