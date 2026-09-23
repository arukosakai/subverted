using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;
using Subverted.App.ViewModels;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>
/// A real ListBox bound the way the view binds it, so the resync's survival is checked against
/// what the control actually does to its selection rather than what a test assumes it does.
/// </summary>
public sealed class SelectionBindingTests
{
    [Test]
    public async Task A_bound_list_keeps_a_replaced_row_selected_through_the_resync()
    {
        var (selectedIsNewRecord, listSelectsIt, asked, paneWentBlank) =
            await HeadlessApp.Session.Dispatch(
                async () =>
                {
                    var diffs = new FakeWorkingCopyDiff();
                    var status = new FakeWorkingCopyStatus()
                        .Answers(Listing(Entry("a.png"), Entry("b.png")))
                        .Answers(Listing(Entry("a.png"), Entry("b.png", NodeStatus.Conflicted)));
                    var view = new WorkingCopyViewModel(
                        "/studio/game",
                        status,
                        DiffPanes.Pane(diffs)
                    );
                    await view.RefreshAsync(CancellationToken.None);

                    var list = new ListBox { ItemsSource = view.Changes };
                    list.Bind(
                        ListBox.SelectedItemProperty,
                        new Binding(nameof(WorkingCopyViewModel.SelectedChange))
                        {
                            Source = view,
                            Mode = BindingMode.TwoWay,
                        }
                    );
                    var window = new Window { Content = list };
                    window.Show();
                    list.SelectedIndex = 1;
                    Dispatcher.UIThread.RunJobs();
                    var states = new List<DiffPaneState>();
                    view.Diff.PropertyChanged += (_, _) => states.Add(view.Diff.State);

                    await view.RefreshAsync(CancellationToken.None);
                    Dispatcher.UIThread.RunJobs();

                    (bool, bool, int, bool) result = (
                        ReferenceEquals(view.SelectedChange, view.Changes[1]),
                        ReferenceEquals(list.SelectedItem, view.Changes[1]),
                        diffs.Paths.Count,
                        states.Contains(DiffPaneState.NothingSelected)
                    );
                    window.Close();
                    return result;
                },
                CancellationToken.None
            );

        await Assert.That(selectedIsNewRecord).IsTrue();
        await Assert.That(listSelectsIt).IsTrue();
        await Assert.That(asked).IsEqualTo(1);
        await Assert.That(paneWentBlank).IsFalse();
    }
}
