using Subverted.App.Presentation;
using Subverted.App.ViewModels;
using Subverted.Core;
using Subverted.Protocol;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

public sealed class WorkingCopyViewModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Test]
    public async Task Before_any_answer_it_is_loading_and_not_clean()
    {
        var view = View("/studio/game/art", new FakeWorkingCopyStatus());

        await Assert.That(view.State).IsEqualTo(WorkingCopyState.Loading);
        await Assert.That(view.IsClean).IsFalse();
        await Assert.That(view.Name).IsEqualTo("art");
    }

    [Test]
    public async Task A_listing_is_shown_sorted_by_path_with_its_summary()
    {
        var status = new FakeWorkingCopyStatus().Answers(
            Listing(Entry("src/b.cs"), Entry("art/a.png", NodeStatus.Added))
        );
        var view = View("/studio/game", status);

        await view.RefreshAsync(None);

        await Assert.That(view.State).IsEqualTo(WorkingCopyState.Ready);
        await Assert
            .That(string.Join(",", view.Changes.Select(row => row.RelPath)))
            .IsEqualTo("art/a.png,src/b.cs");
        await Assert
            .That(string.Join(",", view.Summary.Select(count => count.Text)))
            .IsEqualTo("1 modified,1 added");
        await Assert.That(view.RootPath).IsEqualTo(Info.RootPath);
        await Assert.That(view.RepositoryRoot).IsEqualTo(Info.RepositoryRoot);
        await Assert.That(view.Name).IsEqualTo("game");
        await Assert.That(view.IsClean).IsFalse();
    }

    /// <summary>Writes are aimed at the root once it is known, and at the opened path until then.</summary>
    [Test]
    public async Task The_location_is_the_opened_path_until_the_root_is_known()
    {
        var status = new FakeWorkingCopyStatus().IsUnreachable().Answers(Listing());
        var view = View("/studio/game/art", status);

        await view.RefreshAsync(None);
        var beforeAnyAnswer = view.Location;
        await view.RefreshAsync(None);

        await Assert.That(beforeAnyAnswer).IsEqualTo("/studio/game/art");
        await Assert.That(view.Location).IsEqualTo(Info.RootPath);
    }

    [Test]
    public async Task It_asks_about_the_path_that_was_opened()
    {
        var status = new FakeWorkingCopyStatus();
        var view = View("/studio/game/art", status);

        await view.RefreshAsync(None);

        await Assert.That(status.Paths).IsEquivalentTo(new[] { "/studio/game/art" });
    }

    [Test]
    public async Task An_empty_listing_is_clean()
    {
        var view = View("/studio/game", new FakeWorkingCopyStatus().Answers(Listing()));

        await view.RefreshAsync(None);

        await Assert.That(view.IsClean).IsTrue();
        await Assert.That(view.Headline).IsNull();
    }

    /// <summary>
    /// A locked, unchanged file is nothing to commit, so the copy is clean — but it is still on
    /// screen, since that line is where its lock is released from.
    /// </summary>
    [Test]
    public async Task A_listing_of_only_locked_untouched_files_is_clean_and_still_shows_them()
    {
        var status = new FakeWorkingCopyStatus().Answers(
            Listing(Entry("a.png", NodeStatus.Unmodified, hasLockToken: true))
        );
        var view = View("/studio/game", status);

        await view.RefreshAsync(None);

        await Assert.That(view.Summary).IsEmpty();
        await Assert.That(view.IsClean).IsTrue();
        await Assert.That(view.ShowsCleanMessage).IsFalse();
        await Assert.That(view.HasLines).IsTrue();
        await Assert.That(view.Changes).IsEmpty();
    }

    [Test]
    [Arguments(
        DaemonErrorKind.NotAWorkingCopy,
        WorkingCopyState.NotAWorkingCopy,
        "Not a working copy"
    )]
    [Arguments(
        DaemonErrorKind.WorkingCopyUnreadable,
        WorkingCopyState.Failed,
        "Status could not be read"
    )]
    [Arguments(
        DaemonErrorKind.SvnCommandFailed,
        WorkingCopyState.Failed,
        "Status could not be read"
    )]
    public async Task A_refusal_from_the_daemon_is_shown_as_what_it_is(
        DaemonErrorKind kind,
        WorkingCopyState state,
        string headline
    )
    {
        var status = new FakeWorkingCopyStatus().Answers(new ErrorResponse(kind, "the reason"));
        var view = View("/elsewhere", status);

        await view.RefreshAsync(None);

        await Assert.That(view.State).IsEqualTo(state);
        await Assert.That(view.Headline).IsEqualTo(headline);
        await Assert.That(view.Message).IsEqualTo("the reason");
        await Assert.That(view.IsBlocked).IsTrue();
    }

    [Test]
    public async Task No_daemon_is_unreachable_rather_than_empty()
    {
        var view = View(
            "/studio/game",
            new FakeWorkingCopyStatus().IsUnreachable("connection refused")
        );

        await view.RefreshAsync(None);

        await Assert.That(view.State).IsEqualTo(WorkingCopyState.Unreachable);
        await Assert.That(view.Headline).IsEqualTo("The daemon is not answering");
        await Assert.That(view.Message).IsEqualTo("connection refused");
        await Assert.That(view.IsClean).IsFalse();
        await Assert.That(view.IsBlocked).IsTrue();
        await Assert.That(view.IsStale).IsFalse();
    }

    /// <summary>
    /// The daemon restarting for a second must not make someone's changes vanish: the last listing
    /// stays, marked stale, and comes back live on the next answer.
    /// </summary>
    [Test]
    public async Task A_failure_after_a_listing_keeps_it_marked_as_stale()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("a.png")))
            .IsUnreachable()
            .Answers(Listing(Entry("a.png")));
        var view = View("/studio/game", status);

        await view.RefreshAsync(None);
        await view.RefreshAsync(None);

        await Assert.That(view.Changes.Count).IsEqualTo(1);
        await Assert.That(view.IsStale).IsTrue();
        await Assert.That(view.IsBlocked).IsFalse();

        await view.RefreshAsync(None);

        await Assert.That(view.IsStale).IsFalse();
        await Assert.That(view.Message).IsNull();
        await Assert.That(view.State).IsEqualTo(WorkingCopyState.Ready);
    }

    [Test]
    public async Task An_answer_that_is_not_a_listing_is_a_failure_not_a_crash()
    {
        var status = new FakeWorkingCopyStatus().Answers(new AcknowledgedResponse());
        var view = View("/studio/game", status);

        await view.RefreshAsync(None);

        await Assert.That(view.State).IsEqualTo(WorkingCopyState.Failed);
        await Assert.That(view.Message).Contains(nameof(AcknowledgedResponse));
    }

    /// <summary>The panes, the tree and the composer only show while there is something to act on.</summary>
    [Test]
    public async Task There_are_changes_only_once_a_listing_holds_a_row()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("a.png")))
            .Answers(Listing());
        var view = View("/studio/game", status);
        var beforeAnyAnswer = view.HasChanges;

        await view.RefreshAsync(None);
        var withARow = view.HasChanges;
        await view.RefreshAsync(None);

        await Assert.That(beforeAnyAnswer).IsFalse();
        await Assert.That(withARow).IsTrue();
        await Assert.That(view.HasChanges).IsFalse();
    }

    [Test]
    public async Task A_lock_on_an_untouched_file_is_no_change_to_commit()
    {
        var status = new FakeWorkingCopyStatus().Answers(
            Listing(Entry("a.png", NodeStatus.Unmodified, hasLockToken: true))
        );
        var view = View("/studio/game", status);

        await view.RefreshAsync(None);

        await Assert.That(view.HasChanges).IsFalse();
    }

    [Test]
    public async Task A_lock_on_an_edited_file_is_still_a_change_to_commit()
    {
        var status = new FakeWorkingCopyStatus().Answers(
            Listing(Entry("a.png", hasLockToken: true))
        );
        var view = View("/studio/game", status);

        await view.RefreshAsync(None);

        await Assert.That(view.HasChanges).IsTrue();
    }

    [Test]
    public async Task A_stale_listing_still_has_its_changes_but_a_blocked_one_has_none()
    {
        var stale = View(
            "/studio/game",
            new FakeWorkingCopyStatus().Answers(Listing(Entry("a.png"))).IsUnreachable()
        );
        var blocked = View("/studio/game", new FakeWorkingCopyStatus().IsUnreachable());

        await stale.RefreshAsync(None);
        await stale.RefreshAsync(None);
        await blocked.RefreshAsync(None);

        await Assert.That(stale.IsStale).IsTrue();
        await Assert.That(stale.HasChanges).IsTrue();
        await Assert.That(blocked.IsBlocked).IsTrue();
        await Assert.That(blocked.HasChanges).IsFalse();
    }

    /// <summary>The derived flags drive visibility, so each has to announce itself when it moves.</summary>
    [Test]
    public async Task Every_flag_the_view_binds_to_is_announced_when_an_answer_arrives()
    {
        var view = View("/studio/game", new FakeWorkingCopyStatus().Answers(Listing()));
        var announced = new List<string>();
        view.PropertyChanged += (_, change) => announced.Add(change.PropertyName!);

        await view.RefreshAsync(None);

        await Assert
            .That(announced)
            .Contains(nameof(WorkingCopyViewModel.IsClean))
            .And.Contains(nameof(WorkingCopyViewModel.IsBlocked))
            .And.Contains(nameof(WorkingCopyViewModel.IsStale))
            .And.Contains(nameof(WorkingCopyViewModel.HasChanges))
            .And.Contains(nameof(WorkingCopyViewModel.Headline));
    }

    [Test]
    public async Task A_row_keeps_its_badge_from_the_listing()
    {
        var status = new FakeWorkingCopyStatus().Answers(
            Listing(Entry("a.png", NodeStatus.Modified, isConflicted: true))
        );
        var view = View("/studio/game", status);

        await view.RefreshAsync(None);

        await Assert.That(view.Changes[0].Badge.Tone).IsEqualTo(ChangeTone.Conflict);
    }

    private static WorkingCopyViewModel View(string path, IWorkingCopyStatus status) =>
        WorkingCopies.View(status, path: path);
}
