using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Subverted.App.Infrastructure;
using Subverted.App.ViewModels;
using Subverted.App.Views;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>
/// The real view, keys pressed on the headless platform, so what is checked is what the ListBox
/// and the view's own handler do with a key — not what a test assumes they do.
/// </summary>
public sealed class WorkingCopyViewTests
{
    [Test]
    public async Task Down_moves_the_selection_and_the_diff_follows_it()
    {
        var (selected, diffRow) = await OnViewAsync(
            (window, list, view) =>
            {
                Focus(list, 0);
                window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                return (view.SelectedEntry?.Key, view.Diff.Row?.RelPath);
            }
        );

        await Assert.That(selected).IsEqualTo("b.png");
        await Assert.That(diffRow).IsEqualTo("b.png");
    }

    [Test]
    public async Task Space_ticks_the_selected_line_and_space_again_unticks_it()
    {
        var (afterOne, afterTwo, stillSelected) = await OnViewAsync(
            (window, list, view) =>
            {
                Focus(list, 1);
                window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
                var once = string.Join(",", view.Ticked);
                window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
                return (once, view.Ticked.Count, view.SelectedEntry?.Key);
            }
        );

        await Assert.That(afterOne).IsEqualTo("b.png");
        await Assert.That(afterTwo).IsEqualTo(0);
        await Assert.That(stillSelected).IsEqualTo("b.png");
    }

    [Test]
    public async Task Enter_opens_the_selected_file()
    {
        var launcher = new FakeFileLauncher();

        await OnViewAsync(
            (window, list, _) =>
            {
                Focus(list, 0);
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                return 0;
            },
            launcher
        );

        await Assert
            .That(launcher.Opened)
            .IsEquivalentTo(new[] { DiffTarget.PathOf(Info.RootPath, "a.png") });
    }

    /// <summary>With a modifier held, the keys are someone else's — Ctrl+Space is not a tick.</summary>
    [Test]
    public async Task A_key_with_a_modifier_is_left_alone()
    {
        var ticked = await OnViewAsync(
            (window, list, view) =>
            {
                Focus(list, 0);
                window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.Control);
                return view.Ticked.Count;
            }
        );

        await Assert.That(ticked).IsEqualTo(0);
    }

    [Test]
    public async Task Enter_on_a_folder_line_opens_nothing()
    {
        var launcher = new FakeFileLauncher();

        await OnViewAsync(
            (window, list, view) =>
            {
                view.ShowTreeCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                Focus(list, view.Entries.IndexOf(view.Entries.Single(e => e.Key == "sub/")));
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                return 0;
            },
            launcher
        );

        await Assert.That(launcher.Opened).IsEmpty();
    }

    [Test]
    public async Task Each_line_is_named_for_a_screen_reader_by_what_it_shows()
    {
        var names = await OnViewAsync(
            (_, list, _) =>
                string.Join(
                    "|",
                    list.GetVisualDescendants()
                        .OfType<ListBoxItem>()
                        .Select(item => AutomationProperties.GetName(item))
                )
        );

        await Assert
            .That(names)
            .IsEqualTo("a.png, Modified|b.png, Modified|hero.png in sub, Added");
    }

    [Test]
    public async Task A_filter_that_hides_lines_says_how_many()
    {
        var (shown, text, visible) = await OnViewAsync(
            (_, list, view) =>
            {
                view.Filter = "sub";
                Dispatcher.UIThread.RunJobs();
                var line = list.FindAncestorOfType<WorkingCopyView>()!
                    .FindControl<TextBlock>("HiddenLine")!;
                return (
                    list.GetVisualDescendants().OfType<ListBoxItem>().Count(),
                    line.Text,
                    line.IsEffectivelyVisible
                );
            }
        );

        await Assert.That(shown).IsEqualTo(1);
        await Assert.That(text).IsEqualTo("2 changes hidden");
        await Assert.That(visible).IsTrue();
    }

    [Test]
    public async Task The_context_menu_names_the_platform_s_file_manager_and_holds_history_back()
    {
        var (headers, historyEnabled) = await OnViewAsync(
            (_, list, _) =>
            {
                Focus(list, 0);
                var menu = list.ContextMenu!;
                menu.Open(list);
                Dispatcher.UIThread.RunJobs();
                var items = menu.Items.OfType<MenuItem>().ToList();
                var result = (
                    string.Join("|", items.Select(item => item.Header)),
                    items.Last().IsEffectivelyEnabled
                );
                menu.Close();
                return result;
            }
        );

        await Assert
            .That(headers)
            .IsEqualTo(
                $"Open|{RevealMenuText.For(SystemFileRevealer.ThisPlatform)}|Copy path|History of this file"
            );
        await Assert.That(historyEnabled).IsFalse();
    }

    private static void Focus(ListBox list, int index)
    {
        list.SelectedIndex = index;
        list.ContainerFromIndex(index)!.Focus();
        Dispatcher.UIThread.RunJobs();
    }

    private static Task<T> OnViewAsync<T>(
        Func<Window, ListBox, WorkingCopyViewModel, T> act,
        FakeFileLauncher? launcher = null
    ) =>
        HeadlessApp.Session.Dispatch(
            async () =>
            {
                var status = new FakeWorkingCopyStatus().Answers(
                    Listing(Entry("a.png"), Entry("b.png"), Entry("sub/hero.png", NodeStatus.Added))
                );
                var view = WorkingCopies.View(status, launcher: launcher);
                await view.RefreshAsync(CancellationToken.None);
                var control = new WorkingCopyView { DataContext = view };
                var window = new Window
                {
                    Width = 1100,
                    Height = 700,
                    Content = control,
                };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var list = control.FindControl<ListBox>("List")!;
                var result = act(window, list, view);
                window.Close();
                return result;
            },
            CancellationToken.None
        );
}
