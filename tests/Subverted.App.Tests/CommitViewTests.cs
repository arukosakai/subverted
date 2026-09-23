using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Subverted.App.ViewModels;
using Subverted.App.Views;
using Subverted.Core;
using Subverted.Protocol;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>
/// The commit box, the tick boxes and the revert question in the real view, keys pressed on the
/// headless platform — so Ctrl+Enter is known to reach the command, not assumed to.
/// </summary>
public sealed class CommitViewTests
{
    [Test]
    public async Task Ctrl_enter_in_the_message_box_commits()
    {
        var commits = new FakeWorkingCopyCommit();

        var sent = await OnViewAsync(
            commits,
            Listing(Entry("a.png")),
            (window, control, view) =>
            {
                var box = control
                    .GetLogicalDescendants()
                    .OfType<TextBox>()
                    .Single(b => b.Name == "MessageBox");
                box.Focus();
                window.KeyTextInput("Fix it");
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
                Dispatcher.UIThread.RunJobs();
                return commits.Commits.Select(commit => commit.Message).ToList();
            }
        );

        await Assert.That(sent).IsEquivalentTo(new[] { "Fix it" });
    }

    [Test]
    public async Task Ctrl_enter_from_the_list_commits_too()
    {
        var commits = new FakeWorkingCopyCommit();

        var sent = await OnViewAsync(
            commits,
            Listing(Entry("a.png")),
            (window, control, view) =>
            {
                view.Composer.Message = "From the list";
                var list = Named<ListBox>(control, "List");
                list.SelectedIndex = 0;
                list.ContainerFromIndex(0)!.Focus();
                Dispatcher.UIThread.RunJobs();
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
                Dispatcher.UIThread.RunJobs();
                return commits.Commits.Count;
            }
        );

        await Assert.That(sent).IsEqualTo(1);
    }

    [Test]
    public async Task The_button_counts_the_ticks_and_waits_for_a_message()
    {
        var (text, before, after) = await OnViewAsync(
            new FakeWorkingCopyCommit(),
            Listing(Entry("a.png"), Entry("b.png"), Entry("c.log", NodeStatus.Unversioned)),
            (_, control, view) =>
            {
                var button = Named<Button>(control, "CommitButton");
                var enabledBefore = button.IsEffectivelyEnabled;
                view.Composer.Message = "Two";
                Dispatcher.UIThread.RunJobs();
                return ((string?)button.Content, enabledBefore, button.IsEffectivelyEnabled);
            }
        );

        await Assert.That(text).IsEqualTo("Commit 2 files");
        await Assert.That(before).IsFalse();
        await Assert.That(after).IsTrue();
    }

    [Test]
    public async Task A_refusal_shows_its_notice_under_the_box()
    {
        var commits = new FakeWorkingCopyCommit().Answers(
            new ErrorResponse(DaemonErrorKind.RequestRefused, "'a.png' is in conflict.")
        );

        var (headline, detail) = await OnViewAsync(
            commits,
            Listing(Entry("a.png")),
            (_, control, view) =>
            {
                view.Composer.Message = "Try";
                view.Composer.CommitCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                var notice = Named<NoticeView>(control, "CommitNotice");
                return (
                    Named<TextBlock>(notice, "Headline").Text,
                    Named<SelectableTextBlock>(notice, "Detail").Text
                );
            }
        );

        await Assert.That(headline).IsEqualTo("Nothing was committed");
        await Assert.That(detail).IsEqualTo("'a.png' is in conflict.");
    }

    [Test]
    public async Task A_line_its_folder_decides_has_a_disabled_box_that_is_neither_ticked_nor_not()
    {
        var (enabled, isChecked, folderEnabled) = await OnViewAsync(
            new FakeWorkingCopyCommit(),
            Listing(
                Entry("gone", NodeStatus.Deleted, kind: NodeKind.Directory),
                Entry("gone/a.png", NodeStatus.Deleted)
            ),
            (_, control, _) =>
            {
                var boxes = control
                    .GetVisualDescendants()
                    .OfType<CheckBox>()
                    .Where(box => box.IsVisible)
                    .ToList();
                return (boxes[1].IsEnabled, boxes[1].IsChecked, boxes[0].IsEnabled);
            }
        );

        await Assert.That(enabled).IsFalse();
        await Assert.That(isChecked).IsNull();
        await Assert.That(folderEnabled).IsTrue();
    }

    [Test]
    public async Task A_rename_line_says_where_it_came_from()
    {
        var text = await OnViewAsync(
            new FakeWorkingCopyCommit(),
            Listing(
                [new UnrecordedMove("art/hero.png", "art/protagonist.png")],
                RenameHalves("art/hero.png", "art/protagonist.png")
            ),
            (_, control, _) =>
                string.Join(
                    "|",
                    control
                        .GetVisualDescendants()
                        .OfType<TextBlock>()
                        .Select(block => block.Inlines?.Text)
                        .Where(inlines => inlines is not null)
                )
        );

        await Assert.That(text).Contains("protagonist.png ← hero.png");
    }

    [Test]
    public async Task The_revert_question_covers_the_list_until_escape_cancels_it()
    {
        var (shown, title, afterEscape) = await OnViewAsync(
            new FakeWorkingCopyCommit(),
            Listing(Entry("art/a.png")),
            (window, control, view) =>
            {
                view.RevertCommand.Execute(view.Entries[0]);
                Dispatcher.UIThread.RunJobs();
                var prompt = control.GetVisualDescendants().OfType<RevertPromptView>().Single();
                var wasShown = prompt.IsVisible;
                var titleText = Named<TextBlock>(prompt, "Title").Text;
                Named<Button>(prompt, "CancelButton").Focus();
                window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                return (wasShown, titleText, prompt.IsVisible);
            }
        );

        await Assert.That(shown).IsTrue();
        await Assert.That(title).IsEqualTo("Revert art/a.png?");
        await Assert.That(afterEscape).IsFalse();
    }

    private static T Named<T>(Control root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static Task<T> OnViewAsync<T>(
        FakeWorkingCopyCommit commits,
        StatusResponse listing,
        Func<Window, Control, WorkingCopyViewModel, T> act
    ) =>
        HeadlessApp.Session.Dispatch(
            async () =>
            {
                var view = WorkingCopies.View(
                    new FakeWorkingCopyStatus().Answers(listing),
                    commits: commits
                );
                await view.RefreshAsync(CancellationToken.None);
                // The table and the commit box side by side, as the window's bottom strip holds them.
                var control = new DockPanel();
                var composer = new CommitComposerView { DataContext = view.Composer, Height = 200 };
                DockPanel.SetDock(composer, Dock.Bottom);
                control.Children.Add(composer);
                control.Children.Add(new WorkingCopyView { DataContext = view });
                var window = new Window
                {
                    Width = 1100,
                    Height = 800,
                    Content = control,
                };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var result = act(window, control, view);
                window.Close();
                return result;
            },
            CancellationToken.None
        );
}
