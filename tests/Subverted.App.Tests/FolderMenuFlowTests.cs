using Subverted.App.ViewModels;
using Subverted.Core;
using Subverted.Protocol;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>
/// Revert… and Delete… from the directory pane's menu: the very prompts a line's menu starts, aimed
/// at a folder that may have no line of its own, one question at a time between the two menus.
/// </summary>
public sealed class FolderMenuFlowTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeWorkingCopyRevert _reverts = new();
    private readonly FakeWorkingCopyDeletion _deletions = new();

    [Test]
    public async Task Each_folder_is_offered_what_its_rules_allow()
    {
        var view = await ListedAsync(
            Listing(
                Entry("art/a.png"),
                Entry("docs/build.log", NodeStatus.Unversioned),
                Entry("gone", NodeStatus.Missing, kind: NodeKind.Directory),
                Entry("gone/b.png", NodeStatus.Missing),
                Entry("old", NodeStatus.Deleted, kind: NodeKind.Directory),
                Entry("old/c.png", NodeStatus.Deleted)
            )
        );

        var offered = string.Join(
            ",",
            view.Folders.Select(folder =>
                $"{folder.Content.RelPath}:"
                + $"{Offered(view.RevertFolderCommand.CanExecute(folder), "revert")}/"
                + $"{Offered(view.DeleteFolderCommand.CanExecute(folder), "delete")}"
            )
        );

        await Assert
            .That(offered)
            .IsEqualTo(":revert/-,art:revert/delete,docs:-/delete,gone:revert/delete,old:revert/-");
        await Assert.That(view.RevertFolderCommand.CanExecute(null)).IsFalse();
        await Assert.That(view.DeleteFolderCommand.CanExecute(null)).IsFalse();
    }

    [Test]
    public async Task A_clean_folder_listed_with_every_file_is_offered_delete_but_not_revert()
    {
        var view = await ListedAsync(
            Listing(Entry("art/a.png"), Entry("src/b.cs", NodeStatus.Unmodified))
        );
        await view.ListAllCommand.ExecuteAsync(null);

        var src = FolderOf(view, "src");

        await Assert.That(view.DeleteFolderCommand.CanExecute(src)).IsTrue();
        await Assert.That(view.RevertFolderCommand.CanExecute(src)).IsFalse();
    }

    [Test]
    public async Task Above_the_opened_folder_revert_is_greyed_while_delete_stays()
    {
        var view = await ListedAsync(
            Listing(Entry("art/chars/hero.png")),
            path: Path.Join(Info.RootPath, "art", "chars")
        );

        var offered = string.Join(
            ",",
            view.Folders.Select(folder =>
                $"{folder.Content.RelPath}:"
                + $"{Offered(view.RevertFolderCommand.CanExecute(folder), "revert")}/"
                + $"{Offered(view.DeleteFolderCommand.CanExecute(folder), "delete")}"
            )
        );

        await Assert.That(offered).IsEqualTo(":-/-,art:-/delete,art/chars:revert/delete");
    }

    [Test]
    public async Task A_folder_that_gains_a_change_is_offered_revert_on_the_next_listing()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("art/a.png", NodeStatus.Unversioned)))
            .Answers(Listing(Entry("art/a.png", NodeStatus.Unversioned), Entry("art/b.png")));
        var view = WorkingCopies.View(status, reverts: _reverts, deletions: _deletions);
        await view.RefreshAsync(None);
        var before = view.RevertFolderCommand.CanExecute(FolderOf(view, "art"));
        var raised = 0;
        view.RevertFolderCommand.CanExecuteChanged += (_, _) => raised++;

        await view.RefreshAsync(None);

        await Assert.That(before).IsFalse();
        await Assert.That(raised).IsGreaterThan(0);
        await Assert.That(view.RevertFolderCommand.CanExecute(FolderOf(view, "art"))).IsTrue();
    }

    [Test]
    public async Task Revert_on_a_folder_asks_with_everything_beneath_it_and_sends_nothing()
    {
        var view = await ListedAsync(
            Listing(Entry("art/a.png"), Entry("art/sub/b.png", NodeStatus.Added), Entry("c.png"))
        );

        view.RevertFolderCommand.Execute(FolderOf(view, "art"));

        await Assert.That(view.RevertPrompt.Pending!.Target).IsEqualTo("art");
        await Assert
            .That(string.Join(",", view.RevertPrompt.Pending.Lines.Select(line => line.RelPath)))
            .IsEqualTo("art/a.png,art/sub/b.png");
        await Assert.That(_reverts.Reverted).IsEmpty();
    }

    [Test]
    public async Task Confirming_a_folder_s_revert_sends_the_folder_s_absolute_path()
    {
        var view = await ListedAsync(Listing(Entry("art/a.png"), Entry("art/sub/b.png")));
        view.RevertFolderCommand.Execute(FolderOf(view, "art"));

        await view.RevertPrompt.ConfirmCommand.ExecuteAsync(null);

        await Assert
            .That(_reverts.Reverted)
            .IsEquivalentTo(new[] { DiffTarget.PathOf(Info.RootPath, "art") });
    }

    [Test]
    public async Task A_folder_s_revert_list_that_grew_under_the_question_is_asked_again()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("art/a.png")))
            .Answers(Listing(Entry("art/a.png"), Entry("art/new.psd")));
        var view = WorkingCopies.View(status, reverts: _reverts);
        await view.RefreshAsync(None);
        view.RevertFolderCommand.Execute(FolderOf(view, "art"));
        await view.RefreshAsync(None);

        await view.RevertPrompt.ConfirmCommand.ExecuteAsync(null);

        await Assert.That(_reverts.Reverted).IsEmpty();
        await Assert.That(view.RevertPrompt.HasChangedSinceAsked).IsTrue();
        await Assert
            .That(string.Join(",", view.RevertPrompt.Pending!.Lines.Select(line => line.RelPath)))
            .IsEqualTo("art/a.png,art/new.psd");
    }

    [Test]
    public async Task A_folder_reverted_elsewhere_under_the_question_sends_nothing_and_says_so()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("art/a.png")))
            .Answers(Listing(Entry("src/b.cs")));
        var view = WorkingCopies.View(status, reverts: _reverts);
        await view.RefreshAsync(None);
        view.RevertFolderCommand.Execute(FolderOf(view, "art"));
        await view.RefreshAsync(None);

        await view.RevertPrompt.ConfirmCommand.ExecuteAsync(null);

        await Assert.That(_reverts.Reverted).IsEmpty();
        await Assert.That(view.RevertPrompt.IsAsking).IsFalse();
        await Assert.That(view.RevertPrompt.Notice).IsEqualTo(RevertNotices.NothingLeft("art"));
    }

    [Test]
    public async Task Delete_on_a_folder_reads_it_afresh_and_lists_its_clean_contents()
    {
        _deletions.Lists(
            Listing(
                Entry("src", NodeStatus.Unmodified, kind: NodeKind.Directory),
                Entry("src/b.cs", NodeStatus.Unmodified),
                Entry("src/obj", NodeStatus.Ignored, kind: NodeKind.Directory)
            )
        );
        var view = await ListedAsync(Listing(Entry("art/a.png"), Entry("src/sub/c.cs")));

        await view.DeleteFolderCommand.ExecuteAsync(FolderOf(view, "src"));

        await Assert
            .That(_deletions.Listed)
            .IsEquivalentTo(new[] { DiffTarget.PathOf(Info.RootPath, "src") });
        await Assert.That(view.DeletePrompt.Pending!.Target).IsEqualTo("src");
        await Assert
            .That(
                string.Join(
                    ",",
                    view.DeletePrompt.Pending.Lines.Select(line => line.RelPath).Order()
                )
            )
            .IsEqualTo("src,src/b.cs,src/obj");
        await Assert.That(_deletions.Deleted).IsEmpty();
    }

    [Test]
    public async Task Confirming_a_folder_s_delete_sends_the_folder_s_absolute_path()
    {
        _deletions.Lists(
            Listing(
                Entry("src", NodeStatus.Unmodified, kind: NodeKind.Directory),
                Entry("src/b.cs")
            )
        );
        var view = await ListedAsync(Listing(Entry("src/b.cs")));
        await view.DeleteFolderCommand.ExecuteAsync(FolderOf(view, "src"));

        await view.DeletePrompt.ConfirmCommand.ExecuteAsync(null);

        await Assert
            .That(_deletions.Deleted)
            .IsEquivalentTo(new[] { DiffTarget.PathOf(Info.RootPath, "src") });
    }

    [Test]
    public async Task A_folder_delete_asked_while_a_line_s_delete_is_being_read_is_ignored()
    {
        var release = new TaskCompletionSource();
        _deletions.Lists(Listing(Entry("a.png"), Entry("src/b.cs")), release.Task);
        var view = await ListedAsync(Listing(Entry("a.png"), Entry("src/b.cs")));

        var line = view.DeleteCommand.ExecuteAsync(Line(view, "a.png"));
        var folder = view.DeleteFolderCommand.ExecuteAsync(FolderOf(view, "src"));
        release.SetResult();
        await Task.WhenAll(line, folder);

        await Assert.That(_deletions.Listed).Count().IsEqualTo(1);
        await Assert.That(view.DeletePrompt.Pending!.Target).IsEqualTo("a.png");
    }

    [Test]
    public async Task A_folder_revert_asked_while_a_line_s_revert_runs_is_ignored()
    {
        var release = new TaskCompletionSource();
        _reverts.Answers(new RevertResponse(""), release.Task);
        var view = await ListedAsync(Listing(Entry("a.png"), Entry("art/b.png")));
        view.RevertCommand.Execute(Line(view, "a.png"));

        var running = view.RevertPrompt.ConfirmCommand.ExecuteAsync(null);
        view.RevertFolderCommand.Execute(FolderOf(view, "art"));
        var during = view.RevertPrompt.Pending!.Target;
        release.SetResult();
        await running;

        await Assert.That(during).IsEqualTo("a.png");
        await Assert.That(view.RevertPrompt.IsAsking).IsFalse();
        await Assert
            .That(_reverts.Reverted)
            .IsEquivalentTo(new[] { DiffTarget.PathOf(Info.RootPath, "a.png") });
    }

    private async Task<WorkingCopyViewModel> ListedAsync(
        StatusResponse listing,
        string path = "/studio/game"
    )
    {
        var view = WorkingCopies.View(
            new FakeWorkingCopyStatus().Answers(listing),
            path: path,
            reverts: _reverts,
            deletions: _deletions
        );
        await view.RefreshAsync(None);
        return view;
    }

    private static string Offered(bool isOffered, string item) => isOffered ? item : "-";

    private static FolderEntry FolderOf(WorkingCopyViewModel view, string relPath) =>
        view.Folders.Single(folder => folder.Content.RelPath == relPath);

    private static ChangeListEntry Line(WorkingCopyViewModel view, string key) =>
        view.Entries.Single(entry => entry.Key == key);
}
