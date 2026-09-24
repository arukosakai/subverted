using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Subverted.App.ViewModels;
using Subverted.App.Views;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>The delete question as drawn: over the list, with what is lost, until Escape puts it away.</summary>
public sealed class DeletePromptViewTests
{
    [Test]
    public async Task The_delete_question_covers_the_list_with_its_losses_until_escape_cancels_it()
    {
        var deletions = new FakeWorkingCopyDeletion().Lists(
            Listing(
                Entry(
                    "art",
                    NodeStatus.Unmodified,
                    PropertyStatus.Modified,
                    kind: NodeKind.Directory
                ),
                Entry("art/a.png", NodeStatus.Unmodified),
                Entry("art/notes.txt", NodeStatus.Unversioned)
            )
        );
        var seen = await HeadlessApp.Session.Dispatch(
            async () =>
            {
                var view = WorkingCopies.View(
                    new FakeWorkingCopyStatus().Answers(
                        Listing(
                            Entry(
                                "art",
                                NodeStatus.Unmodified,
                                PropertyStatus.Modified,
                                kind: NodeKind.Directory
                            )
                        )
                    ),
                    deletions: deletions
                );
                await view.RefreshAsync(CancellationToken.None);
                var control = new WorkingCopyView { DataContext = view };
                var window = new Window
                {
                    Width = 1100,
                    Height = 800,
                    Content = control,
                };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                await view.DeleteCommand.ExecuteAsync(view.Entries[0]);
                Dispatcher.UIThread.RunJobs();
                var prompt = control.GetVisualDescendants().OfType<DeletePromptView>().Single();
                var shown = (
                    prompt.IsVisible,
                    Named<TextBlock>(prompt, "Title").Text,
                    Named<TextBlock>(prompt, "Warning").Text,
                    Named<Button>(prompt, "ConfirmButton").Content,
                    Named<ListBox>(prompt, "Lines").ItemCount
                );
                Named<Button>(prompt, "CancelButton").Focus();
                window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                var afterEscape = prompt.IsVisible;
                window.Close();
                return (shown, afterEscape);
            },
            CancellationToken.None
        );

        await Assert
            .That(seen.shown)
            .IsEqualTo(
                (
                    true,
                    (string?)"Delete 3 paths in art?",
                    (string?)"2 of these lose work that cannot be brought back.",
                    (object?)"Delete",
                    3
                )
            );
        await Assert.That(seen.afterEscape).IsFalse();
        await Assert.That(deletions.Deleted).IsEmpty();
    }

    private static T Named<T>(Control root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
}
