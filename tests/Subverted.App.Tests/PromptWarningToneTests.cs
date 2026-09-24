using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Subverted.App.ViewModels;
using Subverted.App.Views;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>A question's warning reads as danger only when something is lost for good.</summary>
public sealed class PromptWarningToneTests
{
    [Test]
    [Arguments(NodeStatus.Modified, true)]
    [Arguments(NodeStatus.Missing, false)]
    public async Task The_revert_warning_is_in_the_losing_tone_only_when_work_is_lost(
        NodeStatus status,
        bool loses
    )
    {
        var tone = await WarningTone(
            new FakeWorkingCopyStatus().Answers(Listing(Entry("a.png", status))),
            deletions: null,
            ask: view =>
            {
                view.RevertCommand.Execute(view.Entries[0]);
                return Task.CompletedTask;
            },
            prompt => prompt.GetVisualDescendants().OfType<RevertPromptView>().Single()
        );

        await Assert.That(tone).IsEqualTo((loses, !loses));
    }

    [Test]
    [Arguments(NodeStatus.Modified, true)]
    [Arguments(NodeStatus.Missing, false)]
    public async Task The_delete_warning_is_in_the_losing_tone_only_when_work_is_lost(
        NodeStatus status,
        bool loses
    )
    {
        var listing = Listing(Entry("a.png", status));
        var tone = await WarningTone(
            new FakeWorkingCopyStatus().Answers(listing),
            new FakeWorkingCopyDeletion().Lists(listing),
            ask: view => view.DeleteCommand.ExecuteAsync(view.Entries[0]),
            prompt => prompt.GetVisualDescendants().OfType<DeletePromptView>().Single()
        );

        await Assert.That(tone).IsEqualTo((loses, !loses));
    }

    private static Task<(bool Loses, bool Muted)> WarningTone(
        FakeWorkingCopyStatus status,
        FakeWorkingCopyDeletion? deletions,
        Func<WorkingCopyViewModel, Task> ask,
        Func<WorkingCopyView, Control> prompt
    ) =>
        HeadlessApp.Session.Dispatch(
            async () =>
            {
                var view = WorkingCopies.View(status, deletions: deletions);
                await view.RefreshAsync(CancellationToken.None);
                var control = new WorkingCopyView { DataContext = view };
                var window = new Window { Content = control };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                await ask(view);
                Dispatcher.UIThread.RunJobs();
                var warning = prompt(control)
                    .GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(block => block.Name == "Warning");
                var tone = (warning.Classes.Contains("loses"), warning.Classes.Contains("muted"));
                window.Close();
                return tone;
            },
            CancellationToken.None
        );
}
