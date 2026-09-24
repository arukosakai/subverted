using Subverted.App.ViewModels;
using Subverted.Core;
using Subverted.Protocol;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>Revert from a line's menu: asked first with the exact list, sent only when confirmed.</summary>
public sealed class RevertFlowTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeWorkingCopyRevert _reverts = new();

    [Test]
    public async Task Revert_is_offered_only_where_it_would_do_something()
    {
        var view = await ListedAsync(
            Listing(
                [new UnrecordedMove("old.png", "new.png")],
                [
                    Entry("art/a.png"),
                    Entry("build.log", NodeStatus.Unversioned),
                    .. RenameHalves("old.png", "new.png"),
                ]
            )
        );
        view.ShowTreeCommand.Execute(null);

        await Assert.That(view.RevertCommand.CanExecute(Line(view, "art/a.png"))).IsTrue();
        await Assert.That(view.RevertCommand.CanExecute(Line(view, "build.log"))).IsFalse();
        await Assert.That(view.RevertCommand.CanExecute(Line(view, "new.png"))).IsFalse();
        await Assert.That(view.RevertCommand.CanExecute(Line(view, "art/"))).IsFalse();
        await Assert.That(view.RevertCommand.CanExecute(null)).IsFalse();
    }

    [Test]
    public async Task Asking_shows_the_list_and_sends_nothing()
    {
        var view = await ListedAsync(Listing(Entry("art/a.png"), Entry("art/b.png")));

        view.RevertCommand.Execute(Line(view, "art/a.png"));

        await Assert.That(view.RevertPrompt.IsAsking).IsTrue();
        await Assert.That(view.RevertPrompt.Pending!.Target).IsEqualTo("art/a.png");
        await Assert
            .That(view.RevertPrompt.Pending.Lines.Select(line => line.RelPath))
            .IsEquivalentTo(new[] { "art/a.png" });
        await Assert.That(_reverts.Reverted).IsEmpty();
    }

    [Test]
    public async Task Cancelling_sends_nothing_and_puts_the_question_away()
    {
        var view = await ListedAsync(Listing(Entry("a.png")));
        view.RevertCommand.Execute(view.Entries[0]);

        view.RevertPrompt.CancelCommand.Execute(null);

        await Assert.That(view.RevertPrompt.IsAsking).IsFalse();
        await Assert.That(view.RevertPrompt.ConfirmCommand.CanExecute(null)).IsFalse();
        await Assert.That(_reverts.Reverted).IsEmpty();
    }

    [Test]
    public async Task Confirming_reverts_the_absolute_path_and_says_so()
    {
        var view = await ListedAsync(Listing(Entry("art/a.png")));
        view.RevertCommand.Execute(view.Entries[0]);
        RevertAttempt? recorded = null;
        view.RevertPrompt.Attempted += attempt => recorded = attempt;

        await view.RevertPrompt.ConfirmCommand.ExecuteAsync(null);

        await Assert
            .That(_reverts.Reverted)
            .IsEquivalentTo(new[] { DiffTarget.PathOf(Info.RootPath, "art/a.png") });
        await Assert.That(view.RevertPrompt.IsAsking).IsFalse();
        await Assert
            .That(view.RevertPrompt.Notice)
            .IsEqualTo(new Notice(NoticeKind.Succeeded, "Reverted art/a.png", null, null));
        await Assert.That(recorded!.Target).IsEqualTo("art/a.png");
        await Assert.That(recorded.Answer).IsTypeOf<RevertResponse>();
    }

    [Test]
    public async Task A_refused_revert_is_uncertain_and_shows_the_daemon_s_text()
    {
        _reverts.Answers(new ErrorResponse(DaemonErrorKind.SvnCommandFailed, "svn: E155004"));
        var view = await ListedAsync(Listing(Entry("a.png")));
        view.RevertCommand.Execute(view.Entries[0]);

        await view.RevertPrompt.ConfirmCommand.ExecuteAsync(null);

        await Assert.That(view.RevertPrompt.Notice!.Kind).IsEqualTo(NoticeKind.Uncertain);
        await Assert.That(view.RevertPrompt.Notice.Detail).IsEqualTo("svn: E155004");
    }

    [Test]
    public async Task An_unreachable_daemon_is_uncertain_and_the_answer_is_recorded_as_absent()
    {
        _reverts.IsUnreachable("gone");
        var view = await ListedAsync(Listing(Entry("a.png")));
        view.RevertCommand.Execute(view.Entries[0]);
        RevertAttempt? recorded = null;
        view.RevertPrompt.Attempted += attempt => recorded = attempt;

        await view.RevertPrompt.ConfirmCommand.ExecuteAsync(null);

        await Assert
            .That(view.RevertPrompt.Notice!.Headline)
            .IsEqualTo("The daemon is not answering");
        await Assert.That(recorded!.Answer).IsNull();
        await Assert.That(view.RevertPrompt.IsReverting).IsFalse();
    }

    /// <summary>The list on screen is the list sent; nothing can swap it while the revert runs.</summary>
    [Test]
    public async Task While_reverting_the_question_can_be_neither_changed_confirmed_nor_cancelled()
    {
        var release = new TaskCompletionSource();
        _reverts.Answers(new RevertResponse(""), release.Task);
        var view = await ListedAsync(Listing(Entry("a.png"), Entry("b.png")));
        view.RevertCommand.Execute(Line(view, "a.png"));

        var running = view.RevertPrompt.ConfirmCommand.ExecuteAsync(null);
        view.RevertCommand.Execute(Line(view, "b.png"));
        var during = (
            view.RevertPrompt.IsReverting,
            view.RevertPrompt.Pending!.Target,
            view.RevertPrompt.ConfirmCommand.CanExecute(null),
            view.RevertPrompt.CancelCommand.CanExecute(null)
        );
        release.SetResult();
        await running;

        await Assert.That(during).IsEqualTo((true, "a.png", false, false));
        await Assert.That(view.RevertPrompt.CancelCommand.CanExecute(null)).IsTrue();
    }

    [Test]
    public async Task Asking_again_puts_the_last_notice_away_and_it_can_be_dismissed()
    {
        var view = await ListedAsync(Listing(Entry("a.png")));
        view.RevertCommand.Execute(view.Entries[0]);
        await view.RevertPrompt.ConfirmCommand.ExecuteAsync(null);
        var afterRevert = view.RevertPrompt.Notice;

        view.RevertCommand.Execute(view.Entries[0]);
        var whileAsking = view.RevertPrompt.Notice;
        await view.RevertPrompt.ConfirmCommand.ExecuteAsync(null);
        view.RevertPrompt.DismissNoticeCommand.Execute(null);

        await Assert.That(afterRevert).IsNotNull();
        await Assert.That(whileAsking).IsNull();
        await Assert.That(view.RevertPrompt.Notice).IsNull();
    }

    /// <summary>
    /// Something saved under a folder while its question was open would be reverted without ever
    /// having been on the list, so the question is put again with the list as it now stands.
    /// </summary>
    [Test]
    public async Task A_list_that_grew_under_the_question_is_asked_again_rather_than_reverted()
    {
        var folder = Entry("art", NodeStatus.Modified, kind: NodeKind.Directory);
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(folder, Entry("art/a.png")))
            .Answers(Listing(folder, Entry("art/a.png"), Entry("art/new.psd")));
        var view = WorkingCopies.View(status, reverts: _reverts);
        await view.RefreshAsync(None);
        view.RevertCommand.Execute(Line(view, "art"));
        await view.RefreshAsync(None);

        await view.RevertPrompt.ConfirmCommand.ExecuteAsync(null);

        await Assert.That(_reverts.Reverted).IsEmpty();
        await Assert.That(view.RevertPrompt.HasChangedSinceAsked).IsTrue();
        await Assert
            .That(view.RevertPrompt.Pending!.Lines.Select(line => line.RelPath))
            .IsEquivalentTo(new[] { "art", "art/a.png", "art/new.psd" });

        await view.RevertPrompt.ConfirmCommand.ExecuteAsync(null);

        await Assert
            .That(_reverts.Reverted)
            .IsEquivalentTo(new[] { DiffTarget.PathOf(Info.RootPath, "art") });
        await Assert.That(view.RevertPrompt.HasChangedSinceAsked).IsFalse();
    }

    [Test]
    public async Task A_list_unchanged_under_the_question_is_reverted_at_once()
    {
        var view = await ListedAsync(Listing(Entry("art/a.png")));
        view.RevertCommand.Execute(Line(view, "art/a.png"));
        await view.RefreshAsync(None);

        await view.RevertPrompt.ConfirmCommand.ExecuteAsync(null);

        await Assert.That(_reverts.Reverted).Count().IsEqualTo(1);
        await Assert.That(view.RevertPrompt.HasChangedSinceAsked).IsFalse();
    }

    [Test]
    public async Task A_target_reverted_elsewhere_under_the_question_sends_nothing_and_says_so()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("art/a.png")))
            .Answers(Listing());
        var view = WorkingCopies.View(status, reverts: _reverts);
        await view.RefreshAsync(None);
        view.RevertCommand.Execute(Line(view, "art/a.png"));
        await view.RefreshAsync(None);

        await view.RevertPrompt.ConfirmCommand.ExecuteAsync(null);

        await Assert.That(_reverts.Reverted).IsEmpty();
        await Assert.That(view.RevertPrompt.IsAsking).IsFalse();
        await Assert
            .That(view.RevertPrompt.Notice)
            .IsEqualTo(
                new Notice(
                    NoticeKind.NothingWritten,
                    "Nothing left to revert in art/a.png",
                    null,
                    null
                )
            );
    }

    /// <summary>A new question starts clean, whatever the last one ended on.</summary>
    [Test]
    public async Task Asking_again_clears_a_changed_list_warning()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("a.png")))
            .Answers(Listing(Entry("a.png", NodeStatus.Missing)));
        var view = WorkingCopies.View(status, reverts: _reverts);
        await view.RefreshAsync(None);
        view.RevertCommand.Execute(Line(view, "a.png"));
        await view.RefreshAsync(None);
        await view.RevertPrompt.ConfirmCommand.ExecuteAsync(null);
        view.RevertPrompt.CancelCommand.Execute(null);

        view.RevertCommand.Execute(Line(view, "a.png"));

        await Assert.That(view.RevertPrompt.HasChangedSinceAsked).IsFalse();
    }

    private async Task<WorkingCopyViewModel> ListedAsync(StatusResponse listing)
    {
        var view = WorkingCopies.View(
            new FakeWorkingCopyStatus().Answers(listing),
            reverts: _reverts
        );
        await view.RefreshAsync(None);
        return view;
    }

    private static ChangeListEntry Line(WorkingCopyViewModel view, string key) =>
        view.Entries.Single(entry => entry.Key == key);
}
