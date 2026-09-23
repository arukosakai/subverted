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
    /// <summary>Its status stays Modified, so it stays where it is; a conflict pins it to the top.</summary>
    [Test]
    [Arguments(NodeStatus.Modified)]
    [Arguments(NodeStatus.Conflicted)]
    public async Task A_bound_list_keeps_a_changed_line_selected_through_the_resync(
        NodeStatus becomes
    )
    {
        var (sameEntry, listSelectsIt, _, asked, paneWentBlank) = await ResyncAsync(becomes);

        await Assert.That(sameEntry).IsTrue();
        await Assert.That(listSelectsIt).IsTrue();
        await Assert.That(asked).IsEqualTo(1);
        await Assert.That(paneWentBlank).IsFalse();
    }

    /// <summary>An updated line keeps its container, and with it the keyboard focus.</summary>
    [Test]
    public async Task A_line_changed_in_place_keeps_its_container()
    {
        var (_, _, sameContainer, _, _) = await ResyncAsync(NodeStatus.Modified);

        await Assert.That(sameContainer).IsTrue();
    }

    private static Task<(bool, bool, bool, int, bool)> ResyncAsync(NodeStatus becomes) =>
        HeadlessApp.Session.Dispatch(
            async () =>
            {
                var diffs = new FakeWorkingCopyDiff();
                var status = new FakeWorkingCopyStatus()
                    .Answers(Listing(Entry("a.png"), Entry("b.png")))
                    .Answers(
                        Listing(Entry("a.png"), Entry("b.png", becomes, PropertyStatus.Modified))
                    );
                var view = WorkingCopies.View(status, DiffPanes.Pane(diffs));
                await view.RefreshAsync(CancellationToken.None);

                var list = new ListBox { ItemsSource = view.Entries };
                list.Bind(
                    ListBox.SelectedItemProperty,
                    new Binding(nameof(WorkingCopyViewModel.SelectedEntry))
                    {
                        Source = view,
                        Mode = BindingMode.TwoWay,
                    }
                );
                var window = new Window { Content = list };
                window.Show();
                list.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();
                var selected = view.SelectedEntry;
                var container = list.ContainerFromItem(selected!);
                var states = new List<DiffPaneState>();
                view.Diff.PropertyChanged += (_, _) => states.Add(view.Diff.State);

                await view.RefreshAsync(CancellationToken.None);
                Dispatcher.UIThread.RunJobs();

                (bool, bool, bool, int, bool) result = (
                    ReferenceEquals(view.SelectedEntry, selected),
                    ReferenceEquals(list.SelectedItem, selected),
                    ReferenceEquals(list.ContainerFromItem(selected!), container),
                    diffs.Paths.Count,
                    states.Contains(DiffPaneState.NothingSelected)
                );
                window.Close();
                return result;
            },
            CancellationToken.None
        );
}
