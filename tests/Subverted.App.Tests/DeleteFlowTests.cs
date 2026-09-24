using Subverted.App.ViewModels;
using Subverted.Core;
using Subverted.Protocol;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>
/// Delete from a line's menu: asked first with the exact list read afresh, unmodified and ignored
/// nodes included, and sent only when confirmed against a list that has not moved since.
/// </summary>
public sealed class DeleteFlowTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeWorkingCopyDeletion _deletions = new();

    [Test]
    public async Task Delete_is_offered_only_on_lines_svn_can_delete_safely()
    {
        var view = await ListedAsync(
            Listing(
                [new UnrecordedMove("old.png", "new.png")],
                [
                    Entry("art/a.png"),
                    Entry("gone.png", NodeStatus.Missing),
                    Entry("build.log", NodeStatus.Unversioned),
                    Entry("old.txt", NodeStatus.Deleted),
                    Entry("clash.png", NodeStatus.Conflicted, isConflicted: true),
                    Entry("obs.png", NodeStatus.Obstructed),
                    .. RenameHalves("old.png", "new.png"),
                ]
            )
        );
        view.ShowTreeCommand.Execute(null);

        var offered = string.Join(
            ",",
            new[]
            {
                "art/a.png",
                "gone.png",
                "build.log",
                "old.txt",
                "clash.png",
                "obs.png",
                "new.png",
                "art/",
            }.Select(key => $"{key}:{view.DeleteCommand.CanExecute(Line(view, key))}")
        );

        await Assert
            .That(offered)
            .IsEqualTo(
                "art/a.png:True,gone.png:True,build.log:False,old.txt:False,clash.png:False,"
                    + "obs.png:False,new.png:False,art/:False"
            );
        await Assert.That(view.DeleteCommand.CanExecute(null)).IsFalse();
    }

    [Test]
    public async Task A_folder_holding_a_half_updated_node_is_not_offered()
    {
        var view = await ListedAsync(
            Listing(
                Entry("art", NodeStatus.Modified, kind: NodeKind.Directory),
                Entry("art/sub", NodeStatus.Incomplete, kind: NodeKind.Directory),
                Entry("docs", NodeStatus.Modified, kind: NodeKind.Directory)
            )
        );

        await Assert.That(view.DeleteCommand.CanExecute(Line(view, "art"))).IsFalse();
        await Assert.That(view.DeleteCommand.CanExecute(Line(view, "docs"))).IsTrue();
    }

    [Test]
    public async Task Asking_reads_the_target_afresh_and_shows_its_list_sending_nothing()
    {
        _deletions.Lists(
            Listing(
                Entry("art", NodeStatus.Unmodified, kind: NodeKind.Directory),
                Entry("art/a.png"),
                Entry("art/b.png", NodeStatus.Unmodified),
                Entry("art/build.log", NodeStatus.Ignored)
            )
        );
        var view = await ListedAsync(
            Listing(Entry("art", NodeStatus.Modified, kind: NodeKind.Directory), Entry("art/a.png"))
        );

        await view.DeleteCommand.ExecuteAsync(Line(view, "art"));

        await Assert
            .That(_deletions.Listed)
            .IsEquivalentTo(new[] { DiffTarget.PathOf(Info.RootPath, "art") });
        await Assert.That(view.DeletePrompt.IsAsking).IsTrue();
        await Assert
            .That(string.Join(",", view.DeletePrompt.Pending!.Lines.Select(line => line.RelPath)))
            .IsEqualTo("art/a.png,art/build.log,art,art/b.png");
        await Assert.That(_deletions.Deleted).IsEmpty();
    }

    [Test]
    public async Task A_target_gone_from_the_fresh_listing_is_not_asked_about()
    {
        _deletions.Lists(Listing(Entry("other.png")));
        var view = await ListedAsync(Listing(Entry("a.png")));

        await view.DeleteCommand.ExecuteAsync(Line(view, "a.png"));

        await Assert.That(view.DeletePrompt.IsAsking).IsFalse();
        await Assert.That(view.DeletePrompt.Notice).IsEqualTo(DeleteNotices.NothingLeft("a.png"));
    }

    [Test]
    public async Task A_target_the_fresh_listing_refuses_is_not_asked_about_and_says_why()
    {
        _deletions.Lists(Listing(Entry("a.png", NodeStatus.Conflicted, isConflicted: true)));
        var view = await ListedAsync(Listing(Entry("a.png")));

        await view.DeleteCommand.ExecuteAsync(Line(view, "a.png"));

        await Assert.That(view.DeletePrompt.IsAsking).IsFalse();
        await Assert.That(view.DeletePrompt.Notice!.Kind).IsEqualTo(NoticeKind.NothingWritten);
        await Assert.That(view.DeletePrompt.Notice.Detail).StartsWith("a.png is in conflict.");
    }

    [Test]
    public async Task A_listing_the_daemon_refused_is_not_asked_about()
    {
        _deletions.Lists(new ErrorResponse(DaemonErrorKind.NotAWorkingCopy, "not a working copy"));
        var view = await ListedAsync(Listing(Entry("a.png")));

        await view.DeleteCommand.ExecuteAsync(Line(view, "a.png"));

        await Assert.That(view.DeletePrompt.IsAsking).IsFalse();
        await Assert
            .That(view.DeletePrompt.Notice)
            .IsEqualTo(DeleteNotices.CouldNotLook("a.png", "not a working copy"));
    }

    [Test]
    public async Task A_listing_answered_with_something_else_is_not_asked_about()
    {
        _deletions.Lists(new DeleteResponse(""));
        var view = await ListedAsync(Listing(Entry("a.png")));

        await view.DeleteCommand.ExecuteAsync(Line(view, "a.png"));

        await Assert
            .That(view.DeletePrompt.Notice)
            .IsEqualTo(
                DeleteNotices.CouldNotLook(
                    "a.png",
                    "The daemon answered with DeleteResponse, which is not a listing."
                )
            );
    }

    [Test]
    public async Task A_listing_no_daemon_answered_is_not_asked_about()
    {
        _deletions.ListingIsUnreachable("gone");
        var view = await ListedAsync(Listing(Entry("a.png")));

        await view.DeleteCommand.ExecuteAsync(Line(view, "a.png"));

        await Assert.That(view.DeletePrompt.IsAsking).IsFalse();
        await Assert
            .That(view.DeletePrompt.Notice)
            .IsEqualTo(DeleteNotices.CouldNotLook("a.png", "gone"));
    }

    [Test]
    public async Task Cancelling_sends_nothing_and_puts_the_question_away()
    {
        _deletions.Lists(Listing(Entry("a.png")));
        var view = await ListedAsync(Listing(Entry("a.png")));
        await view.DeleteCommand.ExecuteAsync(view.Entries[0]);

        view.DeletePrompt.CancelCommand.Execute(null);

        await Assert.That(view.DeletePrompt.IsAsking).IsFalse();
        await Assert.That(view.DeletePrompt.ConfirmCommand.CanExecute(null)).IsFalse();
        await Assert.That(_deletions.Deleted).IsEmpty();
    }

    [Test]
    public async Task Confirming_deletes_the_absolute_path_and_says_so()
    {
        _deletions.Lists(Listing(Entry("art/a.png")));
        var view = await ListedAsync(Listing(Entry("art/a.png")));
        await view.DeleteCommand.ExecuteAsync(view.Entries[0]);
        DeleteAttempt? recorded = null;
        view.DeletePrompt.Attempted += attempt => recorded = attempt;

        await view.DeletePrompt.ConfirmCommand.ExecuteAsync(null);

        await Assert
            .That(_deletions.Deleted)
            .IsEquivalentTo(new[] { DiffTarget.PathOf(Info.RootPath, "art/a.png") });
        await Assert.That(view.DeletePrompt.IsAsking).IsFalse();
        await Assert
            .That(view.DeletePrompt.Notice)
            .IsEqualTo(DeleteNotices.For("art/a.png", new DeleteResponse("")));
        await Assert.That(recorded!.Target).IsEqualTo("art/a.png");
        await Assert.That(recorded.Answer).IsTypeOf<DeleteResponse>();
    }

    [Test]
    public async Task A_refused_delete_is_uncertain_and_shows_the_daemon_s_text()
    {
        _deletions
            .Lists(Listing(Entry("a.png")))
            .Answers(new ErrorResponse(DaemonErrorKind.SvnCommandFailed, "svn: E155004"));
        var view = await ListedAsync(Listing(Entry("a.png")));
        await view.DeleteCommand.ExecuteAsync(view.Entries[0]);

        await view.DeletePrompt.ConfirmCommand.ExecuteAsync(null);

        await Assert.That(view.DeletePrompt.Notice!.Kind).IsEqualTo(NoticeKind.Uncertain);
        await Assert.That(view.DeletePrompt.Notice.Detail).IsEqualTo("svn: E155004");
    }

    [Test]
    public async Task A_daemon_gone_during_the_delete_is_uncertain_and_the_answer_recorded_as_absent()
    {
        _deletions.Lists(Listing(Entry("a.png"))).DeleteIsUnreachable("gone");
        var view = await ListedAsync(Listing(Entry("a.png")));
        await view.DeleteCommand.ExecuteAsync(view.Entries[0]);
        DeleteAttempt? recorded = null;
        view.DeletePrompt.Attempted += attempt => recorded = attempt;

        await view.DeletePrompt.ConfirmCommand.ExecuteAsync(null);

        await Assert.That(view.DeletePrompt.Notice).IsEqualTo(DeleteNotices.Unreachable("gone"));
        await Assert.That(recorded!.Answer).IsNull();
        await Assert.That(view.DeletePrompt.IsDeleting).IsFalse();
    }

    /// <summary>The list on screen is the list sent; nothing can swap it while the delete runs.</summary>
    [Test]
    public async Task While_deleting_the_question_can_be_neither_changed_confirmed_nor_cancelled()
    {
        var release = new TaskCompletionSource();
        _deletions
            .Lists(Listing(Entry("a.png"), Entry("b.png")))
            .Answers(new DeleteResponse(""), release.Task);
        var view = await ListedAsync(Listing(Entry("a.png"), Entry("b.png")));
        await view.DeleteCommand.ExecuteAsync(Line(view, "a.png"));

        var running = view.DeletePrompt.ConfirmCommand.ExecuteAsync(null);
        await view.DeleteCommand.ExecuteAsync(Line(view, "b.png"));
        var during = (
            view.DeletePrompt.IsDeleting,
            view.DeletePrompt.Pending!.Target,
            view.DeletePrompt.ConfirmCommand.CanExecute(null),
            view.DeletePrompt.CancelCommand.CanExecute(null)
        );
        release.SetResult();
        await running;

        await Assert.That(during).IsEqualTo((true, "a.png", false, false));
        await Assert.That(view.DeletePrompt.CancelCommand.CanExecute(null)).IsTrue();
        await Assert.That(_deletions.Deleted).Count().IsEqualTo(1);
    }

    [Test]
    public async Task A_second_question_while_the_first_is_still_being_read_is_ignored()
    {
        var release = new TaskCompletionSource();
        _deletions.Lists(Listing(Entry("a.png"), Entry("b.png")), release.Task);
        var view = await ListedAsync(Listing(Entry("a.png"), Entry("b.png")));

        var first = view.DeletePrompt.AskAsync(Info.RootPath, "a.png", None);
        var second = view.DeletePrompt.AskAsync(Info.RootPath, "b.png", None);
        release.SetResult();
        await Task.WhenAll(first, second);

        await Assert.That(_deletions.Listed).Count().IsEqualTo(1);
        await Assert.That(view.DeletePrompt.Pending!.Target).IsEqualTo("a.png");
    }

    [Test]
    public async Task Asking_again_puts_the_last_notice_away_and_it_can_be_dismissed()
    {
        _deletions.Lists(Listing(Entry("a.png")));
        var view = await ListedAsync(Listing(Entry("a.png")));
        await view.DeleteCommand.ExecuteAsync(view.Entries[0]);
        await view.DeletePrompt.ConfirmCommand.ExecuteAsync(null);
        var afterDelete = view.DeletePrompt.Notice;

        await view.DeleteCommand.ExecuteAsync(view.Entries[0]);
        var whileAsking = view.DeletePrompt.Notice;
        await view.DeletePrompt.ConfirmCommand.ExecuteAsync(null);
        view.DeletePrompt.DismissNoticeCommand.Execute(null);

        await Assert.That(afterDelete).IsNotNull();
        await Assert.That(whileAsking).IsNull();
        await Assert.That(view.DeletePrompt.Notice).IsNull();
    }

    /// <summary>
    /// A file saved into a folder while its question was open would be deleted without ever having
    /// been on the list, so the question is put again with the list as it now stands.
    /// </summary>
    [Test]
    public async Task A_list_that_grew_under_the_question_is_asked_again_rather_than_deleted()
    {
        var folder = Entry("art", NodeStatus.Unmodified, kind: NodeKind.Directory);
        _deletions
            .Lists(Listing(folder, Entry("art/a.png")))
            .Lists(
                Listing(folder, Entry("art/a.png"), Entry("art/new.psd", NodeStatus.Unversioned))
            );
        var view = await ListedAsync(
            Listing(Entry("art", NodeStatus.Modified, kind: NodeKind.Directory))
        );
        await view.DeleteCommand.ExecuteAsync(Line(view, "art"));

        await view.DeletePrompt.ConfirmCommand.ExecuteAsync(null);

        await Assert.That(_deletions.Deleted).IsEmpty();
        await Assert.That(view.DeletePrompt.HasChangedSinceAsked).IsTrue();
        await Assert
            .That(string.Join(",", view.DeletePrompt.Pending!.Lines.Select(line => line.RelPath)))
            .IsEqualTo("art/a.png,art/new.psd,art");

        await view.DeletePrompt.ConfirmCommand.ExecuteAsync(null);

        await Assert
            .That(_deletions.Deleted)
            .IsEquivalentTo(new[] { DiffTarget.PathOf(Info.RootPath, "art") });
        await Assert.That(view.DeletePrompt.HasChangedSinceAsked).IsFalse();
    }

    [Test]
    public async Task A_list_unchanged_under_the_question_is_deleted_at_once()
    {
        _deletions.Lists(Listing(Entry("art/a.png")));
        var view = await ListedAsync(Listing(Entry("art/a.png")));
        await view.DeleteCommand.ExecuteAsync(Line(view, "art/a.png"));

        await view.DeletePrompt.ConfirmCommand.ExecuteAsync(null);

        await Assert.That(_deletions.Listed).Count().IsEqualTo(2);
        await Assert.That(_deletions.Deleted).Count().IsEqualTo(1);
        await Assert.That(view.DeletePrompt.HasChangedSinceAsked).IsFalse();
    }

    [Test]
    public async Task A_target_deleted_elsewhere_under_the_question_sends_nothing_and_says_so()
    {
        _deletions
            .Lists(Listing(Entry("a.png")))
            .Lists(Listing(Entry("a.png", NodeStatus.Deleted)));
        var view = await ListedAsync(Listing(Entry("a.png")));
        await view.DeleteCommand.ExecuteAsync(Line(view, "a.png"));

        await view.DeletePrompt.ConfirmCommand.ExecuteAsync(null);

        await Assert.That(_deletions.Deleted).IsEmpty();
        await Assert.That(view.DeletePrompt.IsAsking).IsFalse();
        await Assert
            .That(view.DeletePrompt.Notice)
            .IsEqualTo(DeleteNotices.Refused("a.png", "a.png is already marked deleted."));
    }

    [Test]
    public async Task A_target_gone_under_the_question_sends_nothing_and_says_so()
    {
        _deletions.Lists(Listing(Entry("a.png"))).Lists(Listing());
        var view = await ListedAsync(Listing(Entry("a.png")));
        await view.DeleteCommand.ExecuteAsync(Line(view, "a.png"));

        await view.DeletePrompt.ConfirmCommand.ExecuteAsync(null);

        await Assert.That(_deletions.Deleted).IsEmpty();
        await Assert.That(view.DeletePrompt.IsAsking).IsFalse();
        await Assert.That(view.DeletePrompt.Notice).IsEqualTo(DeleteNotices.NothingLeft("a.png"));
    }

    [Test]
    public async Task A_daemon_gone_when_checking_the_list_again_sends_nothing()
    {
        _deletions.Lists(Listing(Entry("a.png"))).ListingIsUnreachable("gone");
        var view = await ListedAsync(Listing(Entry("a.png")));
        await view.DeleteCommand.ExecuteAsync(Line(view, "a.png"));
        var attempts = 0;
        view.DeletePrompt.Attempted += _ => attempts++;

        await view.DeletePrompt.ConfirmCommand.ExecuteAsync(null);

        await Assert.That(_deletions.Deleted).IsEmpty();
        await Assert.That(view.DeletePrompt.IsAsking).IsFalse();
        await Assert.That(view.DeletePrompt.IsDeleting).IsFalse();
        await Assert.That(attempts).IsEqualTo(0);
        await Assert
            .That(view.DeletePrompt.Notice)
            .IsEqualTo(DeleteNotices.CouldNotLook("a.png", "gone"));
    }

    /// <summary>A new question starts clean, whatever the last one ended on.</summary>
    [Test]
    public async Task Asking_again_clears_a_changed_list_warning()
    {
        _deletions
            .Lists(Listing(Entry("a.png")))
            .Lists(Listing(Entry("a.png", NodeStatus.Unmodified)))
            .Lists(Listing(Entry("a.png")));
        var view = await ListedAsync(Listing(Entry("a.png")));
        await view.DeleteCommand.ExecuteAsync(Line(view, "a.png"));
        await view.DeletePrompt.ConfirmCommand.ExecuteAsync(null);
        var changed = view.DeletePrompt.HasChangedSinceAsked;
        view.DeletePrompt.CancelCommand.Execute(null);

        await view.DeleteCommand.ExecuteAsync(Line(view, "a.png"));

        await Assert.That(changed).IsTrue();
        await Assert.That(view.DeletePrompt.HasChangedSinceAsked).IsFalse();
    }

    private async Task<WorkingCopyViewModel> ListedAsync(StatusResponse listing)
    {
        var view = WorkingCopies.View(
            new FakeWorkingCopyStatus().Answers(listing),
            deletions: _deletions
        );
        await view.RefreshAsync(None);
        return view;
    }

    private static ChangeListEntry Line(WorkingCopyViewModel view, string key) =>
        view.Entries.Single(entry => entry.Key == key);
}
