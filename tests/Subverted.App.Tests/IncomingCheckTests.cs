using Subverted.App.ViewModels;
using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.App.Tests;

/// <summary>
/// The count on the Update button: what the server says an update would bring, never a number
/// left over from before an update or a failed check.
/// </summary>
public sealed class IncomingCheckTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeWorkingCopyUpdate _updates = new();

    [Test]
    public async Task Nothing_is_known_about_the_server_before_it_is_asked()
    {
        var view = View();

        await Assert.That(view.Updater.Incoming).IsNull();
        await Assert.That(_updates.AskedIncoming).IsEmpty();
    }

    [Test]
    public async Task A_check_asks_about_the_folder_that_was_opened()
    {
        var view = View("/studio/game/art");

        await view.Updater.CheckIncomingAsync(None);

        await Assert.That(_updates.AskedIncoming).IsEquivalentTo(new[] { "/studio/game/art" });
    }

    /// <summary>A new file marks its folder too; the count is the file, as <c>IncomingCount</c> says.</summary>
    [Test]
    public async Task A_check_counts_what_an_update_would_bring_and_badges_the_button()
    {
        _updates.AnswersIncoming(
            new IncomingChange("art/villain.png", PathChange.Added, false),
            new IncomingChange("art", PathChange.Modified, false),
            new IncomingChange("src/a.txt", PathChange.Modified, false)
        );
        var view = View();

        await view.Updater.CheckIncomingAsync(None);

        await Assert.That(view.Updater.Incoming).IsEqualTo(2);
        await Assert.That(view.Updater.IncomingBadge).IsEqualTo("2");
        await Assert
            .That(view.Updater.IncomingSentence)
            .IsEqualTo("2 changes on the server to bring in");
    }

    [Test]
    public async Task A_server_with_nothing_new_shows_no_badge_but_says_so()
    {
        var view = View();

        await view.Updater.CheckIncomingAsync(None);

        await Assert.That(view.Updater.Incoming).IsEqualTo(0);
        await Assert.That(view.Updater.IncomingBadge).IsNull();
        await Assert.That(view.Updater.IncomingSentence).IsEqualTo("Nothing new on the server");
    }

    /// <summary>An old count shown as current would say work is waiting that may have come down.</summary>
    [Test]
    public async Task A_server_that_refuses_clears_the_last_count_and_raises_nothing()
    {
        _updates
            .AnswersIncoming(new IncomingChange("a.txt", PathChange.Modified, false))
            .AnswersIncoming(
                new ErrorResponse(
                    DaemonErrorKind.SvnCommandFailed,
                    "svn: E170013: Unable to connect"
                )
            );
        var view = View();
        var raised = 0;
        view.Updater.Attempted += _ => raised++;
        await view.Updater.CheckIncomingAsync(None);

        await view.Updater.CheckIncomingAsync(None);

        await Assert.That(view.Updater.Incoming).IsNull();
        await Assert.That(view.Updater.Notice).IsNull();
        await Assert.That(raised).IsEqualTo(0);
    }

    [Test]
    public async Task No_daemon_clears_the_last_count_too()
    {
        _updates
            .AnswersIncoming(new IncomingChange("a.txt", PathChange.Modified, false))
            .IncomingIsUnreachable();
        var view = View();
        await view.Updater.CheckIncomingAsync(None);

        await view.Updater.CheckIncomingAsync(None);

        await Assert.That(view.Updater.Incoming).IsNull();
    }

    [Test]
    public async Task No_check_is_sent_while_an_update_runs()
    {
        var release = new TaskCompletionSource();
        _updates.Answers(new UpdateResponse(2, 0, 0, ""), release.Task);
        var view = View();
        var running = view.Updater.UpdateCommand.ExecuteAsync(null);

        await view.Updater.CheckIncomingAsync(None);

        await Assert.That(_updates.AskedIncoming).IsEmpty();
        release.SetResult();
        await running;
    }

    [Test]
    public async Task Starting_an_update_takes_the_count_away()
    {
        var release = new TaskCompletionSource();
        _updates
            .AnswersIncoming(new IncomingChange("a.txt", PathChange.Modified, false))
            .Answers(new UpdateResponse(2, 0, 0, ""), release.Task);
        var view = View();
        await view.Updater.CheckIncomingAsync(None);

        var running = view.Updater.UpdateCommand.ExecuteAsync(null);

        await Assert.That(view.Updater.Incoming).IsNull();
        release.SetResult();
        await running;
    }

    /// <summary>A path SVN skipped is still waiting, so the count after an update is asked, not assumed.</summary>
    [Test]
    public async Task An_update_asks_again_once_it_has_finished()
    {
        _updates.AnswersIncoming(new IncomingChange("skipped.png", PathChange.Modified, false));
        var view = View();

        await view.Updater.UpdateCommand.ExecuteAsync(null);

        await Assert.That(_updates.AskedIncoming).IsEquivalentTo(new[] { "/studio/game" });
        await Assert.That(view.Updater.Incoming).IsEqualTo(1);
    }

    /// <summary>
    /// The command counts as running until it returns, so a check awaited inside it would keep the
    /// button greyed for a whole server round trip after the update had already answered.
    /// </summary>
    [Test]
    public async Task The_button_comes_back_without_waiting_for_the_check_after_an_update()
    {
        var held = new TaskCompletionSource();
        _updates.AnswersIncoming(new IncomingResponse(new IncomingChanges(2, [])), held.Task);
        var view = View();

        await view.Updater.UpdateCommand.ExecuteAsync(null);

        await Assert.That(view.Updater.UpdateCommand.CanExecute(null)).IsTrue();
        held.SetResult();
    }

    /// <summary>
    /// Asked before an update and answered after it, the count describes a copy that is no longer
    /// there: it would badge the button with work the update just brought down.
    /// </summary>
    [Test]
    public async Task A_check_answered_after_an_update_started_is_dropped()
    {
        var held = new TaskCompletionSource();
        var updating = new TaskCompletionSource();
        _updates
            .AnswersIncoming(OneModified(), held.Task)
            .Answers(new UpdateResponse(2, 0, 0, ""), updating.Task);
        var view = View();
        var early = view.Updater.CheckIncomingAsync(None);
        var running = view.Updater.UpdateCommand.ExecuteAsync(null);

        held.SetResult();
        await early;

        await Assert.That(view.Updater.Incoming).IsNull();
        updating.SetResult();
        await running;
    }

    [Test]
    public async Task Of_two_checks_in_flight_the_later_one_wins_whichever_answers_last()
    {
        var held = new TaskCompletionSource();
        _updates
            .AnswersIncoming(OneModified(), held.Task)
            .AnswersIncoming(new IncomingResponse(new IncomingChanges(3, [])));
        var view = View();
        var first = view.Updater.CheckIncomingAsync(None);

        await view.Updater.CheckIncomingAsync(None);
        held.SetResult();
        await first;

        await Assert.That(view.Updater.Incoming).IsEqualTo(0);
    }

    private static IncomingResponse OneModified() =>
        new(new IncomingChanges(2, [new IncomingChange("a.txt", PathChange.Modified, false)]));

    private WorkingCopyViewModel View(string path = "/studio/game") =>
        WorkingCopies.View(new FakeWorkingCopyStatus(), path: path, updates: _updates);
}
