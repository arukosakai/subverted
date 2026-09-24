using Microsoft.Extensions.Time.Testing;
using Subverted.App.ViewModels;
using Subverted.Protocol;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>Update from the title bar: sent at once for the opened folder, one at a time, and logged.</summary>
public sealed class UpdateFlowTests
{
    private readonly FakeWorkingCopyUpdate _updates = new();

    [Test]
    public async Task Update_sends_the_folder_that_was_opened_not_the_root()
    {
        var view = View("/studio/game/art");

        await view.Updater.UpdateCommand.ExecuteAsync(null);

        await Assert.That(_updates.Updated).IsEquivalentTo(new[] { "/studio/game/art" });
    }

    [Test]
    public async Task What_it_came_to_is_shown_and_raised_once()
    {
        var answer = new UpdateResponse(9, 1, 0, "C    a.png");
        _updates.Answers(answer);
        var view = View();
        List<UpdateAttempt> raised = [];
        view.Updater.Attempted += raised.Add;

        await view.Updater.UpdateCommand.ExecuteAsync(null);

        var expected = UpdateNotices.For(answer);
        await Assert.That(view.Updater.Notice).IsEqualTo(expected);
        await Assert.That(raised).IsEquivalentTo([new UpdateAttempt("game", expected, answer)]);
    }

    [Test]
    public async Task No_daemon_is_shown_as_uncertain_with_no_answer()
    {
        _updates.IsUnreachable("gone");
        var view = View();
        UpdateAttempt? raised = null;
        view.Updater.Attempted += attempt => raised = attempt;

        await view.Updater.UpdateCommand.ExecuteAsync(null);

        await Assert.That(view.Updater.Notice).IsEqualTo(UpdateNotices.Unreachable("gone"));
        await Assert
            .That(raised)
            .IsEqualTo(new UpdateAttempt("game", UpdateNotices.Unreachable("gone"), null));
    }

    [Test]
    public async Task A_second_update_cannot_start_while_one_is_running()
    {
        var release = new TaskCompletionSource();
        _updates.Answers(new UpdateResponse(2, 0, 0, ""), release.Task);
        var view = View();

        var running = view.Updater.UpdateCommand.ExecuteAsync(null);

        await Assert.That(view.Updater.IsUpdating).IsTrue();
        await Assert.That(view.Updater.UpdateCommand.CanExecute(null)).IsFalse();
        release.SetResult();
        await running;
        await Assert.That(view.Updater.IsUpdating).IsFalse();
        await Assert.That(view.Updater.UpdateCommand.CanExecute(null)).IsTrue();
    }

    [Test]
    public async Task Starting_an_update_takes_the_last_notice_away()
    {
        var release = new TaskCompletionSource();
        _updates
            .Answers(new UpdateResponse(2, 0, 0, ""))
            .Answers(new UpdateResponse(3, 0, 0, ""), release.Task);
        var view = View();
        await view.Updater.UpdateCommand.ExecuteAsync(null);

        var running = view.Updater.UpdateCommand.ExecuteAsync(null);

        await Assert.That(view.Updater.Notice).IsNull();
        release.SetResult();
        await running;
        await Assert.That(view.Updater.Notice!.Headline).IsEqualTo("Updated to r3");
    }

    [Test]
    public async Task Dismissing_the_notice_puts_it_away()
    {
        var view = View();
        await view.Updater.UpdateCommand.ExecuteAsync(null);

        view.Updater.DismissNoticeCommand.Execute(null);

        await Assert.That(view.Updater.Notice).IsNull();
    }

    [Test]
    public async Task An_update_made_in_the_open_copy_reaches_the_windows_log()
    {
        var clock = new FakeTimeProvider();
        var status = new FakeWorkingCopyStatus().Answers(Listing(Entry("a.png")));
        await using var window = new MainWindowViewModel(
            new FakeRecentStore(),
            new FakeFolderPicker(null),
            path => WorkingCopies.View(status, path: path, updates: _updates),
            clock,
            StringComparison.Ordinal,
            Revisions.View(new FakeRevisionHistory())
        );
        await window.ShowAsync("/studio/game", CancellationToken.None);

        await window.Current!.Updater.UpdateCommand.ExecuteAsync(null);

        await Assert
            .That(window.Log.Lines)
            .IsEquivalentTo([
                new OutputLine(
                    clock.GetLocalNow(),
                    "Update",
                    "game",
                    UpdateNotices.For(new UpdateResponse(1, 0, 0, "At revision 1."))
                ),
            ]);
    }

    private WorkingCopyViewModel View(string path = "/studio/game") =>
        WorkingCopies.View(new FakeWorkingCopyStatus(), path: path, updates: _updates);
}
