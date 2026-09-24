using CommunityToolkit.Mvvm.Input;
using Subverted.App.Presentation;
using Subverted.App.ViewModels;
using Subverted.Core;
using Subverted.Protocol;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>
/// Lock and Unlock from a line's menu: sent at once for the one file, and a refusal shown in SVN's
/// own words, since only they say who holds it.
/// </summary>
public sealed class LockFlowTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private const string HeldByRena =
        "svn: warning: W160035: Path '/art/hero.png' is already locked by user 'rena' in filesystem '/repo/db'";

    private readonly FakeWorkingCopyLocks _locks = new();

    [Test]
    public async Task Lock_is_offered_on_a_versioned_file_and_unlock_only_where_the_lock_is_held()
    {
        var view = await ListedAsync(
            Listing(
                Entry("art/hero.png"),
                Entry("art/button.psd", NodeStatus.Unmodified, hasLockToken: true),
                Entry("art/new.png", NodeStatus.Added),
                Entry("notes.txt", NodeStatus.Unversioned)
            )
        );
        view.ShowTreeCommand.Execute(null);

        var offered = (
            Lock: Offers(view, view.LockCommand),
            Unlock: Offers(view, view.UnlockCommand)
        );

        await Assert.That(offered.Lock).IsEqualTo("art/hero.png");
        await Assert.That(offered.Unlock).IsEqualTo("art/button.psd");
        await Assert.That(view.LockCommand.CanExecute(null)).IsFalse();
        await Assert.That(view.UnlockCommand.CanExecute(null)).IsFalse();
    }

    /// <summary>A folder line in the tree only holds changes; SVN will not lock a directory at all.</summary>
    [Test]
    public async Task A_folder_line_is_offered_neither()
    {
        var view = await ListedAsync(
            Listing(Entry("art/button.psd", NodeStatus.Unmodified, hasLockToken: true))
        );
        view.ShowTreeCommand.Execute(null);

        await Assert.That(view.LockCommand.CanExecute(Line(view, "art/"))).IsFalse();
        await Assert.That(view.UnlockCommand.CanExecute(Line(view, "art/"))).IsFalse();
    }

    [Test]
    public async Task A_resync_that_shows_the_lock_moves_the_offer_from_lock_to_unlock()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("art/hero.png")))
            .Answers(Listing(Entry("art/hero.png", hasLockToken: true)));
        var view = WorkingCopies.View(status, locks: _locks);
        await view.RefreshAsync(None);
        var entry = Line(view, "art/hero.png");
        var raised = new List<string>();
        view.LockCommand.CanExecuteChanged += (_, _) => raised.Add("lock");
        view.UnlockCommand.CanExecuteChanged += (_, _) => raised.Add("unlock");
        var before = (view.LockCommand.CanExecute(entry), view.UnlockCommand.CanExecute(entry));

        await view.RefreshAsync(None);
        var after = (view.LockCommand.CanExecute(entry), view.UnlockCommand.CanExecute(entry));

        await Assert.That(before).IsEqualTo((true, false));
        await Assert.That(after).IsEqualTo((false, true));
        await Assert.That(raised).Contains("lock");
        await Assert.That(raised).Contains("unlock");
    }

    [Test]
    public async Task Locking_sends_the_absolute_path_at_once_and_records_the_attempt()
    {
        var view = await ListedAsync(Listing(Entry("art/hero.png")));
        LockAttempt? recorded = null;
        view.Locker.LockAttempted += attempt => recorded = attempt;

        await view.LockCommand.ExecuteAsync(view.Entries[0]);

        var sent = DiffTarget.PathOf(Info.RootPath, "art/hero.png");
        await Assert.That(_locks.Locked).IsEquivalentTo([sent]);
        await Assert.That(_locks.Unlocked).IsEmpty();
        await Assert.That(view.Locker.Notice!.Kind).IsEqualTo(NoticeKind.Succeeded);
        await Assert.That(view.Locker.Notice.Headline).IsEqualTo("Locked art/hero.png");
        await Assert.That(recorded!.Target).IsEqualTo("art/hero.png");
        await Assert.That(recorded.Notice).IsEqualTo(view.Locker.Notice);
        await Assert.That(recorded.Answer).IsTypeOf<LockResponse>();
    }

    [Test]
    public async Task A_refused_lock_shows_svn_s_warning_naming_the_holder()
    {
        _locks.Answers(new LockResponse("", [HeldByRena]));
        var view = await ListedAsync(Listing(Entry("art/hero.png")));

        await view.LockCommand.ExecuteAsync(view.Entries[0]);

        await Assert.That(view.Locker.Notice!.Kind).IsEqualTo(NoticeKind.NeedsAttention);
        await Assert.That(view.Locker.Notice.Detail).IsEqualTo(HeldByRena);
    }

    [Test]
    public async Task Unlocking_sends_the_absolute_path_at_once_and_records_the_attempt()
    {
        var view = await ListedAsync(
            Listing(Entry("art/button.psd", NodeStatus.Unmodified, hasLockToken: true))
        );
        UnlockAttempt? recorded = null;
        LockAttempt? wrongOne = null;
        view.Locker.UnlockAttempted += attempt => recorded = attempt;
        view.Locker.LockAttempted += attempt => wrongOne = attempt;

        await view.UnlockCommand.ExecuteAsync(view.Entries[0]);

        await Assert
            .That(_locks.Unlocked)
            .IsEquivalentTo([DiffTarget.PathOf(Info.RootPath, "art/button.psd")]);
        await Assert.That(_locks.Locked).IsEmpty();
        await Assert.That(view.Locker.Notice!.Headline).IsEqualTo("Unlocked art/button.psd");
        await Assert.That(recorded!.Target).IsEqualTo("art/button.psd");
        await Assert.That(recorded.Answer).IsTypeOf<UnlockResponse>();
        await Assert.That(wrongOne).IsNull();
    }

    [Test]
    public async Task An_unreachable_daemon_is_uncertain_and_the_answer_is_recorded_as_absent()
    {
        _locks.IsUnreachable("gone");
        var view = await ListedAsync(Listing(Entry("art/hero.png")));
        LockAttempt? recorded = null;
        view.Locker.LockAttempted += attempt => recorded = attempt;

        await view.LockCommand.ExecuteAsync(view.Entries[0]);

        await Assert.That(view.Locker.Notice).IsEqualTo(LockNotices.Unreachable("gone"));
        await Assert.That(recorded!.Answer).IsNull();
        await Assert.That(view.Locker.IsWorking).IsFalse();
    }

    [Test]
    public async Task An_unreachable_daemon_on_unlock_is_recorded_as_an_unlock()
    {
        _locks.IsUnreachable("gone");
        var view = await ListedAsync(
            Listing(Entry("art/button.psd", NodeStatus.Unmodified, hasLockToken: true))
        );
        UnlockAttempt? recorded = null;
        view.Locker.UnlockAttempted += attempt => recorded = attempt;

        await view.UnlockCommand.ExecuteAsync(view.Entries[0]);

        await Assert.That(recorded!.Notice).IsEqualTo(LockNotices.Unreachable("gone"));
        await Assert.That(recorded.Answer).IsNull();
    }

    /// <summary>One at a time: a lock or an unlock asked for while one runs is not sent or queued.</summary>
    [Test]
    public async Task While_one_is_running_another_is_ignored()
    {
        var release = new TaskCompletionSource();
        _locks.Answers(new LockResponse("'a.png' locked by user 'keiichi'.", []), release.Task);
        var view = await ListedAsync(
            Listing(
                Entry("a.png"),
                Entry("b.png"),
                Entry("c.png", NodeStatus.Unmodified, hasLockToken: true)
            )
        );
        var attempts = 0;
        view.Locker.LockAttempted += _ => attempts++;
        view.Locker.UnlockAttempted += _ => attempts++;

        var running = view.LockCommand.ExecuteAsync(Line(view, "a.png"));
        var during = view.Locker.IsWorking;
        await view.Locker.LockAsync("b.png", Info.RootPath, None);
        await view.Locker.UnlockAsync("c.png", Info.RootPath, None);
        release.SetResult();
        await running;

        await Assert.That(during).IsTrue();
        await Assert
            .That(_locks.Locked)
            .IsEquivalentTo([DiffTarget.PathOf(Info.RootPath, "a.png")]);
        await Assert.That(_locks.Unlocked).IsEmpty();
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(view.Locker.IsWorking).IsFalse();
    }

    [Test]
    public async Task Locking_again_puts_the_last_notice_away_until_it_answers_and_it_can_be_dismissed()
    {
        var release = new TaskCompletionSource();
        var view = await ListedAsync(Listing(Entry("a.png")));
        await view.LockCommand.ExecuteAsync(view.Entries[0]);
        var afterFirst = view.Locker.Notice;
        _locks.Answers(new LockResponse("'a.png' locked by user 'keiichi'.", []), release.Task);

        var running = view.LockCommand.ExecuteAsync(view.Entries[0]);
        var whileRunning = view.Locker.Notice;
        release.SetResult();
        await running;
        var afterSecond = view.Locker.Notice;
        view.Locker.DismissNoticeCommand.Execute(null);

        await Assert.That(afterFirst).IsNotNull();
        await Assert.That(whileRunning).IsNull();
        await Assert.That(afterSecond!.Headline).IsEqualTo("Locked a.png");
        await Assert.That(view.Locker.Notice).IsNull();
    }

    [Test]
    public async Task A_lock_and_an_unlock_made_in_the_open_copy_reach_the_windows_log()
    {
        var status = new FakeWorkingCopyStatus().Answers(
            Listing(Entry("a.png"), Entry("b.png", NodeStatus.Unmodified, hasLockToken: true))
        );
        await using var window = new MainWindowViewModel(
            new FakeRecentStore(),
            new FakeFolderPicker(null),
            path => WorkingCopies.View(status, path: path, locks: _locks),
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(),
            StringComparison.Ordinal,
            Revisions.View(new FakeRevisionHistory())
        );
        await window.ShowAsync("/studio/game", None);

        await window.Current!.LockCommand.ExecuteAsync(Line(window.Current, "a.png"));
        await window.Current.UnlockCommand.ExecuteAsync(Line(window.Current, "b.png"));

        await Assert
            .That(window.Log.Lines.Select(line => (line.Operation, line.Subject)))
            .IsEquivalentTo([("Lock", "a.png"), ("Unlock", "b.png")]);
    }

    /// <summary>
    /// Listing changes, a lock taken before starting still has a line to release it from, but
    /// nothing on it is a change: no tick, no count, nothing sent. The edited one beside it is.
    /// </summary>
    [Test]
    public async Task A_lock_on_an_untouched_file_is_listed_to_release_but_counted_and_sent_nowhere()
    {
        var view = await ListedAsync(
            Listing(
                Entry("art/held.psd", NodeStatus.Unmodified, hasLockToken: true),
                Entry("art/worked.psd", hasLockToken: true)
            )
        );
        view.ShowTreeCommand.Execute(null);
        var held = Line(view, "art/held.psd");
        var worked = Line(view, "art/worked.psd");

        await Assert.That(view.Listing).IsEqualTo(ListedNodes.Changes);
        await Assert.That(held.CanTick).IsFalse();
        await Assert.That(view.ToggleTickCommand.CanExecute(held)).IsFalse();
        await Assert.That(view.RevertCommand.CanExecute(held)).IsFalse();
        await Assert.That(view.LockCommand.CanExecute(held)).IsFalse();
        await Assert.That(view.UnlockCommand.CanExecute(held)).IsTrue();
        await Assert
            .That(held.Row!.Badge)
            .IsEqualTo(new ChangeBadge("Unchanged", ChangeTone.Quiet));
        await Assert.That(worked.CanTick).IsTrue();
        await Assert.That(view.Ticked).IsEquivalentTo(["art/worked.psd"]);
        await Assert.That(view.Composer.ButtonText).IsEqualTo(CommitButtonText.For(1));
        await Assert
            .That(string.Join(",", view.Changes.Select(row => row.RelPath)))
            .IsEqualTo("art/worked.psd");
        await Assert
            .That(view.Folders.Select(folder => (folder.Content.RelPath, folder.Content.Count)))
            .IsEquivalentTo([("", 1), ("art", 1)]);
        await Assert
            .That(string.Join(",", view.Summary.Select(count => count.Text)))
            .IsEqualTo("1 modified");
    }

    /// <summary>Even a tick forced past its disabled box sends nothing: the commit set is changes only.</summary>
    [Test]
    public async Task A_tick_on_a_lock_only_line_is_never_sent()
    {
        var view = await ListedAsync(
            Listing(
                Entry("art/held.psd", NodeStatus.Unmodified, hasLockToken: true),
                Entry("art/worked.psd", hasLockToken: true)
            )
        );

        view.ToggleTickCommand.Execute(Line(view, "art/worked.psd"));
        view.ToggleTickCommand.Execute(Line(view, "art/held.psd"));

        await Assert.That(view.Ticked).IsEquivalentTo(["art/held.psd"]);
        await Assert.That(view.Composer.Selection.Sent).IsEmpty();
    }

    private async Task<WorkingCopyViewModel> ListedAsync(StatusResponse listing)
    {
        var view = WorkingCopies.View(new FakeWorkingCopyStatus().Answers(listing), locks: _locks);
        await view.RefreshAsync(None);
        return view;
    }

    private static string Offers(
        WorkingCopyViewModel view,
        IAsyncRelayCommand<ChangeListEntry?> command
    ) =>
        string.Join(
            ",",
            view.Entries.Where(entry => command.CanExecute(entry)).Select(entry => entry.Key)
        );

    private static ChangeListEntry Line(WorkingCopyViewModel view, string key) =>
        view.Entries.Single(entry => entry.Key == key);
}
