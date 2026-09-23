using Microsoft.Extensions.Time.Testing;
using Subverted.App.ViewModels;
using Subverted.Protocol;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>The output log in the bottom strip: every write, with SVN's own words, for the session.</summary>
public sealed class OutputLogTests
{
    private static readonly Notice Done = new(
        NoticeKind.Succeeded,
        "Committed r8",
        "Sending a.png",
        null
    );

    private readonly FakeTimeProvider _clock = new(
        new DateTimeOffset(2030, 1, 1, 9, 30, 5, TimeSpan.Zero)
    );

    [Test]
    public async Task It_starts_empty()
    {
        var log = new OutputLogViewModel(_clock);

        await Assert.That(log.Lines).IsEmpty();
        await Assert.That(log.IsEmpty).IsTrue();
    }

    [Test]
    public async Task A_commit_is_logged_with_its_time_path_count_and_first_message_line()
    {
        var log = new OutputLogViewModel(_clock);

        log.Record(new CommitAttempt(["a.png", "b.png"], "Hero pass\n\nDetails below", Done, null));

        await Assert
            .That(log.Lines)
            .IsEquivalentTo([
                new OutputLine(_clock.GetLocalNow(), "Commit", "2 paths · Hero pass", Done),
            ]);
        await Assert.That(log.IsEmpty).IsFalse();
    }

    [Test]
    [Arguments(1, "1 path")]
    [Arguments(2, "2 paths")]
    [Arguments(1200, "1,200 paths")]
    public async Task A_commit_says_how_many_paths_it_named(int count, string said)
    {
        var log = new OutputLogViewModel(_clock);
        var paths = Enumerable.Range(0, count).Select(index => $"f{index}").ToList();

        log.Record(new CommitAttempt(paths, "m", Done, null));

        await Assert.That(log.Lines[0].Subject).IsEqualTo($"{said} · m");
    }

    [Test]
    public async Task A_message_written_on_windows_loses_its_carriage_return_and_surrounding_space()
    {
        var log = new OutputLogViewModel(_clock);

        log.Record(new CommitAttempt(["a"], "  Fix the door\r\nsecond line", Done, null));

        await Assert.That(log.Lines[0].Subject).IsEqualTo("1 path · Fix the door");
    }

    [Test]
    public async Task A_revert_is_logged_with_its_target()
    {
        var log = new OutputLogViewModel(_clock);
        var reverted = new Notice(NoticeKind.Succeeded, "Reverted", null, null);

        log.Record(new RevertAttempt("art/hero.png", reverted, null));

        await Assert
            .That(log.Lines)
            .IsEquivalentTo([
                new OutputLine(_clock.GetLocalNow(), "Revert", "art/hero.png", reverted),
            ]);
    }

    [Test]
    public async Task Lines_are_kept_oldest_first()
    {
        var log = new OutputLogViewModel(_clock);

        log.Record(new RevertAttempt("first", Done, null));
        _clock.Advance(TimeSpan.FromMinutes(1));
        log.Record(new RevertAttempt("second", Done, null));

        await Assert
            .That(string.Join(",", log.Lines.Select(line => line.Subject)))
            .IsEqualTo("first,second");
        await Assert.That(log.Lines[1].At - log.Lines[0].At).IsEqualTo(TimeSpan.FromMinutes(1));
    }

    [Test]
    public async Task Clearing_empties_it()
    {
        var log = new OutputLogViewModel(_clock);
        log.Record(new RevertAttempt("a", Done, null));

        log.ClearCommand.Execute(null);

        await Assert.That(log.Lines).IsEmpty();
        await Assert.That(log.IsEmpty).IsTrue();
    }

    [Test]
    public async Task A_commit_made_in_the_open_copy_reaches_the_windows_log()
    {
        var commits = new FakeWorkingCopyCommit().Answers(FakeWorkingCopyCommit.Committed(8));
        var status = new FakeWorkingCopyStatus().Answers(Listing(Entry("a.png")));
        await using var window = new MainWindowViewModel(
            new FakeRecentStore(),
            new FakeFolderPicker(null),
            path => WorkingCopies.View(status, path: path, commits: commits),
            _clock,
            StringComparison.Ordinal
        );
        await window.ShowAsync("/studio/game", CancellationToken.None);
        window.Current!.Composer.Message = "Hero";

        await window.Current.Composer.CommitCommand.ExecuteAsync(null);

        await Assert
            .That(window.Log.Lines.Select(line => line.Operation))
            .IsEquivalentTo(["Commit"]);
        await Assert.That(window.Log.Lines[0].Notice.Kind).IsEqualTo(NoticeKind.Succeeded);
    }

    [Test]
    public async Task A_revert_made_in_the_open_copy_reaches_the_windows_log()
    {
        var status = new FakeWorkingCopyStatus().Answers(Listing(Entry("a.png")));
        await using var window = new MainWindowViewModel(
            new FakeRecentStore(),
            new FakeFolderPicker(null),
            path => WorkingCopies.View(status, path: path),
            _clock,
            StringComparison.Ordinal
        );
        await window.ShowAsync("/studio/game", CancellationToken.None);
        window.Current!.RevertCommand.Execute(window.Current.Entries[0]);

        await window.Current.RevertPrompt.ConfirmCommand.ExecuteAsync(null);

        await Assert.That(window.Log.Lines.Select(line => line.Subject)).IsEquivalentTo(["a.png"]);
    }
}
