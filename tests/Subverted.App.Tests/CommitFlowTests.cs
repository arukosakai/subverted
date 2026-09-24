using Subverted.App.ViewModels;
using Subverted.Core;
using Subverted.Protocol;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>Slice 3 through the working copy's view model: defaults, what is sent, and each answer.</summary>
public sealed class CommitFlowTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeWorkingCopyCommit _commits = new();

    [Test]
    public async Task Edits_missing_files_and_renames_start_ticked_and_unversioned_files_do_not()
    {
        var view = await ListedAsync(
            Listing(
                [new UnrecordedMove("art/hero.png", "art/protagonist.png")],
                [
                    Entry("src/a.cs"),
                    Entry("gone.txt", NodeStatus.Missing),
                    Entry("build.log", NodeStatus.Unversioned),
                    .. RenameHalves("art/hero.png", "art/protagonist.png"),
                ]
            )
        );

        await Assert
            .That(view.Ticked)
            .IsEquivalentTo(new[] { "src/a.cs", "gone.txt", "art/protagonist.png" });
        await Assert.That(Line(view, "build.log").IsTicked).IsFalse();
        await Assert.That(Line(view, "art/protagonist.png").IsTicked).IsTrue();
        await Assert.That(view.Entries.Any(entry => entry.Key == "art/hero.png")).IsFalse();
    }

    [Test]
    public async Task An_untick_survives_the_once_a_second_resync()
    {
        var status = new FakeWorkingCopyStatus().Answers(Listing(Entry("a.cs"), Entry("b.cs")));
        var view = View(status);
        await view.RefreshAsync(None);
        view.ToggleTickCommand.Execute(Line(view, "a.cs"));

        await view.RefreshAsync(None);
        await view.RefreshAsync(None);

        await Assert.That(view.Ticked).IsEquivalentTo(new[] { "b.cs" });
        await Assert.That(Line(view, "a.cs").IsTicked).IsFalse();
        await Assert.That(view.Composer.ButtonText).IsEqualTo("Commit 1 file");
    }

    [Test]
    [Arguments("", false)]
    [Arguments("   ", false)]
    [Arguments("Fix the hero", true)]
    public async Task Commit_needs_a_message_that_is_not_blank(string message, bool allowed)
    {
        var view = await ListedAsync(Listing(Entry("a.cs")));

        view.Composer.Message = message;

        await Assert.That(view.Composer.CommitCommand.CanExecute(null)).IsEqualTo(allowed);
    }

    [Test]
    public async Task Commit_needs_something_ticked()
    {
        var view = await ListedAsync(Listing(Entry("new.cs", NodeStatus.Unversioned)));
        view.Composer.Message = "Add it";
        var before = view.Composer.CommitCommand.CanExecute(null);

        view.ToggleTickCommand.Execute(view.Entries[0]);

        await Assert.That(before).IsFalse();
        await Assert.That(view.Composer.ButtonText).IsEqualTo("Commit 1 file");
        await Assert.That(view.Composer.CommitCommand.CanExecute(null)).IsTrue();
    }

    [Test]
    public async Task Commit_sends_the_absolute_paths_of_both_rename_halves_and_the_message()
    {
        var view = await ListedAsync(
            Listing(
                [new UnrecordedMove("art/hero.png", "art/protagonist.png")],
                [Entry("src/a.cs"), .. RenameHalves("art/hero.png", "art/protagonist.png")]
            )
        );
        view.Composer.Message = "Rename the hero";

        await view.Composer.CommitCommand.ExecuteAsync(null);

        var (paths, message) = _commits.Commits.Single();
        await Assert
            .That(paths)
            .IsEquivalentTo(
                new[]
                {
                    DiffTarget.PathOf(Info.RootPath, "art/hero.png"),
                    DiffTarget.PathOf(Info.RootPath, "art/protagonist.png"),
                    DiffTarget.PathOf(Info.RootPath, "src/a.cs"),
                }
            );
        await Assert.That(message).IsEqualTo("Rename the hero");
    }

    /// <summary>The operator's call: the filter hides a tick from the commit, never from the ticks.</summary>
    [Test]
    public async Task A_tick_the_filter_hides_is_not_sent_and_is_sent_again_once_shown()
    {
        var view = await ListedAsync(Listing(Entry("art/a.png"), Entry("src/b.cs")));
        view.Composer.Message = "Art";

        view.Filter = "art";
        var whileFiltered = view.Composer.ButtonText;
        await view.Composer.CommitCommand.ExecuteAsync(null);
        view.Filter = "";

        await Assert.That(whileFiltered).IsEqualTo("Commit 1 file");
        await Assert
            .That(_commits.Commits[0].Paths)
            .IsEquivalentTo(new[] { DiffTarget.PathOf(Info.RootPath, "art/a.png") });
        await Assert.That(view.Ticked).IsEquivalentTo(new[] { "src/b.cs" });
        await Assert.That(view.Composer.ButtonText).IsEqualTo("Commit 1 file");
    }

    [Test]
    public async Task A_rename_is_sent_whole_when_the_filter_matches_only_its_old_name()
    {
        var view = await ListedAsync(
            Listing(
                [new UnrecordedMove("art/hero.png", "art/protagonist.png")],
                [Entry("src/a.cs"), .. RenameHalves("art/hero.png", "art/protagonist.png")]
            )
        );
        view.Composer.Message = "Rename";
        view.Filter = "hero";

        await view.Composer.CommitCommand.ExecuteAsync(null);

        await Assert
            .That(_commits.Commits[0].Paths)
            .IsEquivalentTo(
                new[]
                {
                    DiffTarget.PathOf(Info.RootPath, "art/hero.png"),
                    DiffTarget.PathOf(Info.RootPath, "art/protagonist.png"),
                }
            );
    }

    [Test]
    public async Task A_commit_at_a_revision_clears_the_message_and_the_ticks_it_sent_only()
    {
        _commits.Answers(FakeWorkingCopyCommit.Committed(1825));
        var view = await ListedAsync(Listing(Entry("art/a.png"), Entry("src/b.cs")));
        view.Composer.Message = "Art";
        view.Filter = "art";
        CommitAttempt? recorded = null;
        view.Composer.Attempted += attempt => recorded = attempt;

        await view.Composer.CommitCommand.ExecuteAsync(null);

        await Assert.That(view.Composer.Notice!.Headline).IsEqualTo("Committed r1825");
        await Assert.That(view.Composer.Notice.Kind).IsEqualTo(NoticeKind.Succeeded);
        await Assert.That(view.Composer.Message).IsEqualTo("");
        await Assert.That(view.Ticked).IsEquivalentTo(new[] { "src/b.cs" });
        await Assert.That(Line(view, "art/a.png").IsTicked).IsFalse();
        await Assert.That(recorded!.SentRelPaths).IsEquivalentTo(new[] { "art/a.png" });
        await Assert.That(recorded.Message).IsEqualTo("Art");
        await Assert.That((recorded.Answer as CommitSelectionResponse)!.Revision).IsEqualTo(1825);
    }

    [Test]
    public async Task Nothing_needing_sending_keeps_the_message_and_the_ticks()
    {
        _commits.Answers(FakeWorkingCopyCommit.Committed(revision: null));
        var view = await ListedAsync(Listing(Entry("a.cs")));
        view.Composer.Message = "Again";

        await view.Composer.CommitCommand.ExecuteAsync(null);

        await Assert.That(view.Composer.Notice!.Headline).IsEqualTo("Nothing needed sending");
        await Assert.That(view.Composer.Message).IsEqualTo("Again");
        await Assert.That(view.Ticked).IsEquivalentTo(new[] { "a.cs" });
    }

    /// <summary>
    /// D32: marks were made and nothing was sent. The rows come back as A and D at the same paths,
    /// plus the old half of a rename as its own D, and the retry sends the same set again.
    /// </summary>
    [Test]
    public async Task Failing_after_marking_keeps_everything_and_the_retry_sends_the_same_set()
    {
        var moves = new[] { new UnrecordedMove("hero.png", "protagonist.png") };
        var status = new FakeWorkingCopyStatus()
            .Answers(
                Listing(
                    moves,
                    [
                        Entry("new.cs", NodeStatus.Unversioned),
                        .. RenameHalves("hero.png", "protagonist.png"),
                    ]
                )
            )
            .Answers(
                Listing(
                    Entry("new.cs", NodeStatus.Added),
                    Entry("hero.png", NodeStatus.Deleted),
                    Entry("protagonist.png", NodeStatus.Added, isCopied: true)
                )
            );
        _commits.Answers(
            new SelectionNotCommittedResponse(
                new SelectionSchedule(
                    ["new.cs"],
                    [],
                    [new RecordedMove("hero.png", "protagonist.png")]
                ),
                SelectionStep.Commit,
                "",
                "svn: E165001: Commit blocked by pre-commit hook"
            )
        );
        var view = View(status);
        await view.RefreshAsync(None);
        view.ToggleTickCommand.Execute(Line(view, "new.cs"));
        view.Composer.Message = "Add and rename";

        await view.Composer.CommitCommand.ExecuteAsync(null);
        var notice = view.Composer.Notice!;
        await view.RefreshAsync(None);
        await view.Composer.CommitCommand.ExecuteAsync(null);

        await Assert.That(notice.Kind).IsEqualTo(NoticeKind.LeftMarked);
        await Assert
            .That(notice.Detail)
            .IsEqualTo("svn: E165001: Commit blocked by pre-commit hook");
        await Assert.That(view.Composer.Message).IsEqualTo("");
        await Assert.That(_commits.Commits[1].Paths).IsEquivalentTo(_commits.Commits[0].Paths);
        await Assert.That(_commits.Commits[1].Message).IsEqualTo("Add and rename");
    }

    [Test]
    public async Task A_refusal_keeps_the_message_and_says_nothing_was_written()
    {
        _commits.Answers(
            new ErrorResponse(DaemonErrorKind.RequestRefused, "'a.cs' is in conflict.")
        );
        var view = await ListedAsync(Listing(Entry("a.cs")));
        view.Composer.Message = "Try";

        await view.Composer.CommitCommand.ExecuteAsync(null);

        await Assert.That(view.Composer.Notice!.Kind).IsEqualTo(NoticeKind.NothingWritten);
        await Assert.That(view.Composer.Notice.Detail).IsEqualTo("'a.cs' is in conflict.");
        await Assert.That(view.Composer.Message).IsEqualTo("Try");
        await Assert.That(view.Ticked).IsEquivalentTo(new[] { "a.cs" });
    }

    [Test]
    public async Task An_unreachable_daemon_is_reported_as_uncertain_and_keeps_the_message()
    {
        _commits.IsUnreachable("connection refused");
        var view = await ListedAsync(Listing(Entry("a.cs")));
        view.Composer.Message = "Try";
        CommitAttempt? recorded = null;
        view.Composer.Attempted += attempt => recorded = attempt;

        await view.Composer.CommitCommand.ExecuteAsync(null);

        await Assert.That(view.Composer.Notice!.Kind).IsEqualTo(NoticeKind.Uncertain);
        await Assert.That(view.Composer.Notice.Detail).IsEqualTo("connection refused");
        await Assert.That(view.Composer.Message).IsEqualTo("Try");
        await Assert.That(view.Composer.IsCommitting).IsFalse();
        await Assert.That(recorded!.Answer).IsNull();
    }

    [Test]
    public async Task While_a_commit_is_in_flight_another_cannot_start()
    {
        var release = new TaskCompletionSource();
        _commits.Answers(FakeWorkingCopyCommit.Committed(), release.Task);
        var view = await ListedAsync(Listing(Entry("a.cs"), Entry("b.cs")));
        view.Composer.Message = "Once";

        var inFlight = view.Composer.CommitCommand.ExecuteAsync(null);
        var duringCommit = (
            view.Composer.IsCommitting,
            view.Composer.CommitCommand.CanExecute(null)
        );
        release.SetResult();
        await inFlight;

        await Assert.That(duringCommit).IsEqualTo((true, false));
        await Assert.That(view.Composer.IsCommitting).IsFalse();
        await Assert.That(_commits.Commits.Count).IsEqualTo(1);
    }

    [Test]
    public async Task A_new_commit_puts_the_last_notice_away_and_it_can_be_dismissed()
    {
        var release = new TaskCompletionSource();
        _commits
            .Answers(new ErrorResponse(DaemonErrorKind.RequestRefused, "no"))
            .Answers(FakeWorkingCopyCommit.Committed(), release.Task);
        var view = await ListedAsync(Listing(Entry("a.cs")));
        view.Composer.Message = "Try";
        await view.Composer.CommitCommand.ExecuteAsync(null);

        var second = view.Composer.CommitCommand.ExecuteAsync(null);
        var noticeInFlight = view.Composer.Notice;
        release.SetResult();
        await second;
        view.Composer.DismissNoticeCommand.Execute(null);

        await Assert.That(noticeInFlight).IsNull();
        await Assert.That(view.Composer.Notice).IsNull();
    }

    /// <summary>D20 in the list: beneath an added folder left out, a tick is not a choice.</summary>
    [Test]
    public async Task A_line_its_folder_decides_shows_no_tick_of_its_own_and_cannot_be_ticked()
    {
        var view = await ListedAsync(
            Listing(
                Entry("new", NodeStatus.Added, kind: NodeKind.Directory),
                Entry("new/a.png", NodeStatus.Added)
            )
        );
        var folder = Line(view, "new");
        var child = Line(view, "new/a.png");
        var whileFolderTicked = (child.IsDecidedByFolder, child.TickMark);

        view.ToggleTickCommand.Execute(folder);

        await Assert.That(whileFolderTicked).IsEqualTo((false, (bool?)true));
        await Assert.That(child.IsDecidedByFolder).IsTrue();
        await Assert.That(child.TickMark).IsNull();
        await Assert.That(child.IsTickable).IsFalse();
        await Assert.That(view.ToggleTickCommand.CanExecute(child)).IsFalse();
        await Assert.That(view.Composer.Selection.Sent).IsEmpty();
        await Assert.That(folder.TickMark == false).IsTrue();
    }

    /// <summary>
    /// An update can put a ticked file into conflict. Its tick is held rather than dropped, so the
    /// commit does not offer what SVN would refuse, and it is back once the conflict is resolved.
    /// </summary>
    [Test]
    public async Task A_line_that_goes_into_conflict_holds_its_tick_until_it_is_resolved()
    {
        var view = View(
            new FakeWorkingCopyStatus()
                .Answers(Listing(Entry("a.png")))
                .Answers(Listing(Entry("a.png", NodeStatus.Conflicted, isConflicted: true)))
                .Answers(Listing(Entry("a.png")))
        );
        await view.RefreshAsync(None);
        var before = (Line(view, "a.png").TickMark, view.Composer.Selection.Sent.Count);

        await view.RefreshAsync(None);
        var line = Line(view, "a.png");
        var inConflict = (
            line.TickMark,
            line.IsTickable,
            view.ToggleTickCommand.CanExecute(line),
            view.Composer.Selection.Sent.Count
        );
        await view.RefreshAsync(None);

        await Assert.That(before).IsEqualTo(((bool?)true, 1));
        await Assert.That(inConflict).IsEqualTo(((bool?)false, false, false, 0));
        await Assert.That(Line(view, "a.png").TickMark == true).IsTrue();
        await Assert.That(view.Composer.Selection.Sent.Count).IsEqualTo(1);
    }

    /// <summary>What an update usually leaves: a file first seen conflicted, which a resolve makes ready to send.</summary>
    [Test]
    public async Task A_line_first_seen_in_conflict_is_ticked_once_it_is_resolved()
    {
        var view = View(
            new FakeWorkingCopyStatus()
                .Answers(Listing(Entry("a.png", NodeStatus.Conflicted, isConflicted: true)))
                .Answers(Listing(Entry("a.png")))
        );
        await view.RefreshAsync(None);
        var inConflict = Line(view, "a.png").TickMark;

        await view.RefreshAsync(None);

        await Assert.That(inConflict == false).IsTrue();
        await Assert.That(Line(view, "a.png").TickMark == true).IsTrue();
        await Assert
            .That(view.Composer.Selection.Sent.Select(row => row.RelPath))
            .IsEquivalentTo(["a.png"]);
    }

    private async Task<WorkingCopyViewModel> ListedAsync(StatusResponse listing)
    {
        var view = View(new FakeWorkingCopyStatus().Answers(listing));
        await view.RefreshAsync(None);
        return view;
    }

    private WorkingCopyViewModel View(FakeWorkingCopyStatus status) =>
        WorkingCopies.View(status, commits: _commits);

    private static ChangeListEntry Line(WorkingCopyViewModel view, string key) =>
        view.Entries.Single(entry => entry.Key == key);
}
