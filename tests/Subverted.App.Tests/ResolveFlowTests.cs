using Subverted.App.ViewModels;
using Subverted.Core;
using Subverted.Protocol;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>
/// Resolve from a line's menu: keeping your own version goes at once, taking theirs asks first
/// with the exact list, since that is the one that throws work away.
/// </summary>
public sealed class ResolveFlowTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeWorkingCopyResolve _resolves = new();

    [Test]
    public async Task Resolve_is_offered_only_where_there_is_a_conflict_to_settle()
    {
        var view = await ListedAsync(
            Listing(Conflicted("art/hero.png"), Entry("art/villain.png"), Entry("notes.txt"))
        );
        view.ShowTreeCommand.Execute(null);

        foreach (var command in Commands(view))
        {
            await Assert.That(command.CanExecute(Line(view, "art/hero.png"))).IsTrue();
            await Assert.That(command.CanExecute(Line(view, "art/villain.png"))).IsFalse();
            // A folder line that only holds changes is not a node to resolve.
            await Assert.That(command.CanExecute(Line(view, "art/"))).IsFalse();
            await Assert.That(command.CanExecute(null)).IsFalse();
        }
    }

    /// <summary>The submenu header follows the picked line, and says so when the pick moves.</summary>
    [Test]
    public async Task The_resolve_submenu_is_offered_exactly_while_the_picked_line_has_a_conflict()
    {
        var view = await ListedAsync(Listing(Conflicted("art/hero.png"), Entry("notes.txt")));
        var raised = 0;
        view.PropertyChanged += (_, args) =>
            raised += args.PropertyName == nameof(view.IsResolveOffered) ? 1 : 0;
        var withNothingPicked = view.IsResolveOffered;

        view.SelectedEntry = Line(view, "art/hero.png");
        var onTheConflict = view.IsResolveOffered;
        view.SelectedEntry = Line(view, "notes.txt");

        await Assert.That(withNothingPicked).IsFalse();
        await Assert.That(onTheConflict).IsTrue();
        await Assert.That(view.IsResolveOffered).IsFalse();
        await Assert.That(raised).IsEqualTo(2);
    }

    /// <summary>An update can put the picked line into conflict without the pick moving at all.</summary>
    [Test]
    public async Task A_resync_that_puts_the_picked_line_in_conflict_offers_the_submenu()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("art/hero.png")))
            .Answers(Listing(Conflicted("art/hero.png")));
        var view = WorkingCopies.View(status);
        await view.RefreshAsync(None);
        view.SelectedEntry = Line(view, "art/hero.png");
        var before = view.IsResolveOffered;
        var raised = false;
        view.PropertyChanged += (_, args) =>
            raised |= args.PropertyName == nameof(view.IsResolveOffered);

        await view.RefreshAsync(None);

        await Assert.That(before).IsFalse();
        await Assert.That(view.IsResolveOffered).IsTrue();
        await Assert.That(raised).IsTrue();
    }

    /// <summary>A conflicted folder resolves recursively, so its line is offered even with the conflict below it.</summary>
    [Test]
    public async Task A_listed_folder_with_a_conflict_beneath_it_is_offered()
    {
        var view = await ListedAsync(
            Listing(
                Entry("art", NodeStatus.Modified, kind: NodeKind.Directory),
                Conflicted("art/hero.png")
            )
        );

        await Assert.That(view.KeepMineCommand.CanExecute(Line(view, "art"))).IsTrue();
    }

    [Test]
    [Arguments(ConflictResolution.Mine)]
    [Arguments(ConflictResolution.Working)]
    public async Task Keeping_your_own_version_is_sent_at_once_for_the_absolute_path(
        ConflictResolution kept
    )
    {
        var view = await ListedAsync(Listing(Conflicted("art/hero.png")));
        ResolveAttempt? recorded = null;
        view.Resolver.Attempted += attempt => recorded = attempt;

        await Command(view, kept).ExecuteAsync(view.Entries[0]);

        await Assert
            .That(_resolves.Resolved)
            .IsEquivalentTo([(DiffTarget.PathOf(Info.RootPath, "art/hero.png"), kept)]);
        await Assert.That(view.Resolver.IsAsking).IsFalse();
        await Assert.That(view.Resolver.Notice!.Kind).IsEqualTo(NoticeKind.Succeeded);
        await Assert.That(recorded!.Kept).IsEqualTo(kept);
        await Assert.That(recorded.Target).IsEqualTo("art/hero.png");
        await Assert.That(recorded.Answer).IsTypeOf<ResolveResponse>();
    }

    [Test]
    public async Task Taking_theirs_asks_first_with_the_list_and_sends_nothing()
    {
        var view = await ListedAsync(Listing(Conflicted("art/hero.png")));

        await view.TakeTheirsCommand.ExecuteAsync(view.Entries[0]);

        await Assert.That(view.Resolver.IsAsking).IsTrue();
        await Assert.That(view.Resolver.Pending!.Lines).IsEquivalentTo(["art/hero.png"]);
        await Assert.That(view.Resolver.Pending.Resolution).IsEqualTo(ConflictResolution.Theirs);
        await Assert.That(view.Resolver.ConfirmCommand.CanExecute(null)).IsTrue();
        await Assert.That(_resolves.Resolved).IsEmpty();
    }

    [Test]
    public async Task Confirming_theirs_sends_it_and_puts_the_question_away()
    {
        var view = await ListedAsync(Listing(Conflicted("art/hero.png")));
        await view.TakeTheirsCommand.ExecuteAsync(view.Entries[0]);

        await view.Resolver.ConfirmCommand.ExecuteAsync(null);

        await Assert
            .That(_resolves.Resolved)
            .IsEquivalentTo([
                (DiffTarget.PathOf(Info.RootPath, "art/hero.png"), ConflictResolution.Theirs),
            ]);
        await Assert.That(view.Resolver.IsAsking).IsFalse();
        await Assert.That(view.Resolver.ConfirmCommand.CanExecute(null)).IsFalse();
    }

    [Test]
    public async Task Cancelling_theirs_sends_nothing()
    {
        var view = await ListedAsync(Listing(Conflicted("a.png")));
        await view.TakeTheirsCommand.ExecuteAsync(view.Entries[0]);

        view.Resolver.CancelCommand.Execute(null);

        await Assert.That(view.Resolver.IsAsking).IsFalse();
        await Assert.That(_resolves.Resolved).IsEmpty();
    }

    [Test]
    public async Task An_unreachable_daemon_is_uncertain_and_the_answer_is_recorded_as_absent()
    {
        _resolves.IsUnreachable("gone");
        var view = await ListedAsync(Listing(Conflicted("a.png")));
        ResolveAttempt? recorded = null;
        view.Resolver.Attempted += attempt => recorded = attempt;

        await view.KeepMineCommand.ExecuteAsync(view.Entries[0]);

        await Assert.That(view.Resolver.Notice!.Headline).IsEqualTo("The daemon is not answering");
        await Assert.That(recorded!.Answer).IsNull();
        await Assert.That(view.Resolver.IsResolving).IsFalse();
    }

    /// <summary>One resolve at a time: a second asked for while the first runs is not sent or queued.</summary>
    [Test]
    public async Task While_resolving_another_resolve_is_ignored_and_the_question_is_held()
    {
        var release = new TaskCompletionSource();
        _resolves.Answers(new ResolveResponse(["a.png"], []), release.Task);
        var view = await ListedAsync(Listing(Conflicted("a.png"), Conflicted("b.png")));
        await view.TakeTheirsCommand.ExecuteAsync(Line(view, "a.png"));

        var running = view.Resolver.ConfirmCommand.ExecuteAsync(null);
        await view.KeepMineCommand.ExecuteAsync(Line(view, "b.png"));
        var during = (
            view.Resolver.IsResolving,
            view.Resolver.ConfirmCommand.CanExecute(null),
            view.Resolver.CancelCommand.CanExecute(null)
        );
        release.SetResult();
        await running;

        await Assert.That(during).IsEqualTo((true, false, false));
        await Assert
            .That(_resolves.Resolved.Select(sent => sent.Resolution))
            .IsEquivalentTo([ConflictResolution.Theirs]);
        await Assert.That(view.Resolver.CancelCommand.CanExecute(null)).IsTrue();
    }

    [Test]
    public async Task Resolving_again_puts_the_last_notice_away_and_it_can_be_dismissed()
    {
        var view = await ListedAsync(Listing(Conflicted("a.png")));
        await view.KeepMineCommand.ExecuteAsync(view.Entries[0]);
        var afterFirst = view.Resolver.Notice;

        await view.TakeTheirsCommand.ExecuteAsync(view.Entries[0]);
        var whileAsking = view.Resolver.Notice;
        view.Resolver.CancelCommand.Execute(null);
        await view.KeepMineCommand.ExecuteAsync(view.Entries[0]);
        view.Resolver.DismissNoticeCommand.Execute(null);

        await Assert.That(afterFirst).IsNotNull();
        await Assert.That(whileAsking).IsNull();
        await Assert.That(view.Resolver.Notice).IsNull();
    }

    [Test]
    public async Task A_resolve_made_in_the_open_copy_reaches_the_windows_log()
    {
        var status = new FakeWorkingCopyStatus().Answers(Listing(Conflicted("a.png")));
        await using var window = new MainWindowViewModel(
            new FakeRecentStore(),
            new FakeFolderPicker(null),
            path => WorkingCopies.View(status, path: path, resolves: _resolves),
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(),
            StringComparison.Ordinal,
            Revisions.View(new FakeRevisionHistory())
        );
        await window.ShowAsync("/studio/game", None);

        await window.Current!.KeepMineCommand.ExecuteAsync(window.Current.Entries[0]);

        await Assert
            .That(window.Log.Lines.Select(line => (line.Operation, line.Subject)))
            .IsEquivalentTo([("Resolve", "a.png")]);
    }

    /// <summary>A conflict an update left under the folder after the question was put is not replaced unseen.</summary>
    [Test]
    public async Task A_conflict_list_that_grew_under_the_question_is_asked_again_rather_than_sent()
    {
        var folder = Entry("art", NodeStatus.Modified, kind: NodeKind.Directory);
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(folder, Conflicted("art/a.png")))
            .Answers(Listing(folder, Conflicted("art/a.png"), Conflicted("art/b.png")));
        var view = WorkingCopies.View(status, resolves: _resolves);
        await view.RefreshAsync(None);
        await view.TakeTheirsCommand.ExecuteAsync(Line(view, "art"));
        await view.RefreshAsync(None);

        await view.Resolver.ConfirmCommand.ExecuteAsync(null);

        await Assert.That(_resolves.Resolved).IsEmpty();
        await Assert.That(view.Resolver.HasChangedSinceAsked).IsTrue();
        await Assert
            .That(view.Resolver.Pending!.Lines)
            .IsEquivalentTo(new[] { "art/a.png", "art/b.png" });

        await view.Resolver.ConfirmCommand.ExecuteAsync(null);

        await Assert
            .That(_resolves.Resolved)
            .IsEquivalentTo(
                new[] { (DiffTarget.PathOf(Info.RootPath, "art"), ConflictResolution.Theirs) }
            );
        await Assert.That(view.Resolver.HasChangedSinceAsked).IsFalse();
    }

    [Test]
    public async Task Conflicts_settled_elsewhere_under_the_question_send_nothing_and_say_so()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Conflicted("a.png")))
            .Answers(Listing(Entry("a.png")));
        var view = WorkingCopies.View(status, resolves: _resolves);
        await view.RefreshAsync(None);
        await view.TakeTheirsCommand.ExecuteAsync(Line(view, "a.png"));
        await view.RefreshAsync(None);

        await view.Resolver.ConfirmCommand.ExecuteAsync(null);

        await Assert.That(_resolves.Resolved).IsEmpty();
        await Assert.That(view.Resolver.IsAsking).IsFalse();
        await Assert
            .That(view.Resolver.Notice)
            .IsEqualTo(
                new Notice(
                    NoticeKind.NothingWritten,
                    "Nothing left to resolve in a.png",
                    null,
                    null
                )
            );
    }

    [Test]
    public async Task Cancelling_a_changed_question_clears_its_warning()
    {
        var folder = Entry("art", NodeStatus.Modified, kind: NodeKind.Directory);
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(folder, Conflicted("art/a.png")))
            .Answers(Listing(folder, Conflicted("art/a.png"), Conflicted("art/b.png")));
        var view = WorkingCopies.View(status, resolves: _resolves);
        await view.RefreshAsync(None);
        await view.TakeTheirsCommand.ExecuteAsync(Line(view, "art"));
        await view.RefreshAsync(None);
        await view.Resolver.ConfirmCommand.ExecuteAsync(null);

        view.Resolver.CancelCommand.Execute(null);

        await Assert.That(view.Resolver.HasChangedSinceAsked).IsFalse();
    }

    private async Task<WorkingCopyViewModel> ListedAsync(StatusResponse listing)
    {
        var view = WorkingCopies.View(
            new FakeWorkingCopyStatus().Answers(listing),
            resolves: _resolves
        );
        await view.RefreshAsync(None);
        return view;
    }

    private static WorkingCopyEntry Conflicted(string relPath) =>
        Entry(relPath, NodeStatus.Conflicted, isConflicted: true);

    private static CommunityToolkit.Mvvm.Input.IAsyncRelayCommand<ChangeListEntry?>[] Commands(
        WorkingCopyViewModel view
    ) => [view.KeepMineCommand, view.TakeTheirsCommand, view.MarkResolvedCommand];

    private static CommunityToolkit.Mvvm.Input.IAsyncRelayCommand<ChangeListEntry?> Command(
        WorkingCopyViewModel view,
        ConflictResolution kept
    ) =>
        kept switch
        {
            ConflictResolution.Mine => view.KeepMineCommand,
            ConflictResolution.Working => view.MarkResolvedCommand,
            _ => view.TakeTheirsCommand,
        };

    private static ChangeListEntry Line(WorkingCopyViewModel view, string key) =>
        view.Entries.Single(entry => entry.Key == key);
}
