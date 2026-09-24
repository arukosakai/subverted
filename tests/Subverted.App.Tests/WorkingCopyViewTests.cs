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
            .IsEqualTo("a.png, Not versioned|b.png, Not versioned|hero.png in sub, Not versioned");
    }

    [Test]
    public async Task Each_folder_line_is_named_for_a_screen_reader_by_its_folder_and_count()
    {
        var names = await OnViewAsync(
            (_, list, _) =>
                string.Join(
                    "|",
                    list.FindAncestorOfType<WorkingCopyView>()!
                        .FindControl<ListBox>("Folders")!
                        .GetVisualDescendants()
                        .OfType<ListBoxItem>()
                        .Select(item => AutomationProperties.GetName(item))
                )
        );

        await Assert.That(names).IsEqualTo("game, 3 changes, expanded|sub, 1 change");
    }

    [Test]
    public async Task Each_folder_s_icon_is_drawn_in_the_most_urgent_tone_below_it()
    {
        var status = new FakeWorkingCopyStatus().Answers(
            Listing(
                Entry("a.png", NodeStatus.Conflicted),
                Entry("sub/hero.png", NodeStatus.Deleted)
            )
        );

        var (strokes, conflict, deleted) = await OnViewAsync(
            (_, list, _) =>
            {
                var application = Application.Current!;
                application.TryGetResource(
                    "Tone.Conflict",
                    application.ActualThemeVariant,
                    out var conflictBrush
                );
                application.TryGetResource(
                    "Tone.Deleted",
                    application.ActualThemeVariant,
                    out var deletedBrush
                );
                var icons = list.FindAncestorOfType<WorkingCopyView>()!
                    .FindControl<ListBox>("Folders")!
                    .GetVisualDescendants()
                    .OfType<ListBoxItem>()
                    .Select(item =>
                        item.GetVisualDescendants()
                            .OfType<Avalonia.Controls.Shapes.Path>()
                            .Single(path => path.Name == "FolderIcon")
                            .Stroke
                    )
                    .ToList();
                return (icons, conflictBrush, deletedBrush);
            },
            status: status
        );

        await Assert.That(conflict).IsNotSameReferenceAs(deleted);
        await Assert.That(strokes[0]).IsSameReferenceAs(conflict);
        await Assert.That(strokes[1]).IsSameReferenceAs(deleted);
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
        var (headers, historyEnabled, resolveHeaders) = await OnViewAsync(
            (_, list, _) =>
            {
                Focus(list, 0);
                var menu = list.ContextMenu!;
                menu.Open(list);
                Dispatcher.UIThread.RunJobs();
                var items = menu.Items.OfType<MenuItem>().ToList();
                var resolve = items.Single(item => Equals(item.Header, "Resolve"));
                var result = (
                    string.Join("|", items.Select(item => item.Header)),
                    items.Last().IsEffectivelyEnabled,
                    string.Join("|", resolve.Items.OfType<MenuItem>().Select(item => item.Header))
                );
                menu.Close();
                return result;
            }
        );

        await Assert
            .That(headers)
            .IsEqualTo(
                $"Open|{RevealMenuText.For(FileRevealers.ThisPlatform)}|Copy path|Revert…|Resolve|History of this file"
            );
        await Assert.That(historyEnabled).IsFalse();
        await Assert.That(resolveHeaders).IsEqualTo("Keep mine|Take theirs…|Mark as resolved");
    }

    [Test]
    public async Task Clicking_a_chevron_collapses_its_folder_and_leaves_the_chosen_one_chosen()
    {
        var (lines, chosen, names) = await OnViewAsync(
            (window, list, view) =>
            {
                var folders = FoldersOf(list);
                view.SelectedFolder = FolderOf(view, "src");
                Dispatcher.UIThread.RunJobs();

                Click(window, ChevronOf(folders, FolderOf(view, "art")));

                return (
                    FolderPaths(view),
                    view.SelectedFolder?.Content.RelPath,
                    string.Join("|", ItemNames(folders))
                );
            },
            status: NestedStatus()
        );

        await Assert.That(lines).IsEqualTo(",art,src");
        await Assert.That(chosen).IsEqualTo("src");
        await Assert
            .That(names)
            .IsEqualTo("game, 3 changes, expanded|art, 2 changes, collapsed|src, 1 change");
    }

    [Test]
    public async Task Clicking_a_folder_s_name_chooses_it_without_collapsing_it()
    {
        var (lines, chosen) = await OnViewAsync(
            (window, list, view) =>
            {
                var art = FoldersOf(list).ContainerFromItem(FolderOf(view, "art"))!;
                Click(window, art.GetVisualDescendants().OfType<TextBlock>().First());
                return (FolderPaths(view), view.SelectedFolder?.Content.RelPath);
            },
            status: NestedStatus()
        );

        await Assert.That(lines).IsEqualTo(",art,art/chars,src");
        await Assert.That(chosen).IsEqualTo("art");
    }

    [Test]
    public async Task Collapsing_above_the_chosen_folder_moves_the_choice_up_and_narrows_the_table_to_it()
    {
        var (chosen, keys) = await OnViewAsync(
            (window, list, view) =>
            {
                view.SelectedFolder = FolderOf(view, "art/chars");
                Dispatcher.UIThread.RunJobs();

                Click(window, ChevronOf(FoldersOf(list), FolderOf(view, "art")));

                return (
                    FoldersOf(list).SelectedItem is FolderEntry entry
                        ? entry.Content.RelPath
                        : null,
                    string.Join(",", view.Entries.Select(entry => entry.Key))
                );
            },
            status: NestedStatus()
        );

        await Assert.That(chosen).IsEqualTo("art");
        await Assert.That(keys).IsEqualTo("art/a.png,art/chars/hero.png");
    }

    /// <summary>A leaf has no chevron but keeps its column, so sibling names start at the same x.</summary>
    [Test]
    public async Task A_leaf_folder_s_name_lines_up_with_a_sibling_that_has_a_chevron()
    {
        var (artX, srcX, srcChevronShown) = await OnViewAsync(
            (window, list, view) =>
            {
                var folders = FoldersOf(list);
                double NameX(string relPath) =>
                    folders
                        .ContainerFromItem(FolderOf(view, relPath))!
                        .GetVisualDescendants()
                        .OfType<TextBlock>()
                        .First()
                        .TranslatePoint(default, window)!
                        .Value.X;
                return (
                    NameX("art"),
                    NameX("src"),
                    ChevronOf(folders, FolderOf(view, "src")).IsVisible
                );
            },
            status: NestedStatus()
        );

        await Assert.That(srcX).IsEqualTo(artX);
        await Assert.That(srcChevronShown).IsFalse();
    }

    [Test]
    public async Task Left_collapses_the_focused_folder_and_right_opens_it_again()
    {
        var (afterLeft, afterRight, chosen) = await OnViewAsync(
            (window, list, view) =>
            {
                FocusFolder(FoldersOf(list), view, "art");
                window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
                var collapsed = FolderPaths(view);
                window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
                return (collapsed, FolderPaths(view), view.SelectedFolder?.Content.RelPath);
            },
            status: NestedStatus()
        );

        await Assert.That(afterLeft).IsEqualTo(",art,src");
        await Assert.That(afterRight).IsEqualTo(",art,art/chars,src");
        await Assert.That(chosen).IsEqualTo("art");
    }

    /// <summary>Focus comes along to the parent, so the next ↓ moves from there.</summary>
    [Test]
    public async Task Left_on_a_folder_with_nothing_under_it_chooses_its_parent_and_down_carries_on_from_there()
    {
        var (afterLeft, afterDown) = await OnViewAsync(
            (window, list, view) =>
            {
                FocusFolder(FoldersOf(list), view, "art/chars");
                window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                var parent = view.SelectedFolder?.Content.RelPath;
                window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                return (parent, view.SelectedFolder?.Content.RelPath);
            },
            status: NestedStatus()
        );

        await Assert.That(afterLeft).IsEqualTo("art");
        await Assert.That(afterDown).IsEqualTo("art/chars");
    }

    [Test]
    public async Task Left_with_a_modifier_held_is_left_alone_in_the_folder_pane()
    {
        var lines = await OnViewAsync(
            (window, list, view) =>
            {
                FocusFolder(FoldersOf(list), view, "art");
                window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.Control);
                return FolderPaths(view);
            },
            status: NestedStatus()
        );

        await Assert.That(lines).IsEqualTo(",art,art/chars,src");
    }

    private static FakeWorkingCopyStatus NestedStatus() =>
        new FakeWorkingCopyStatus().Answers(
            Listing(
                Entry("art/a.png", NodeStatus.Unversioned),
                Entry("art/chars/hero.png", NodeStatus.Unversioned),
                Entry("src/b.cs", NodeStatus.Unversioned)
            )
        );

    private static ListBox FoldersOf(ListBox list) =>
        list.FindAncestorOfType<WorkingCopyView>()!.FindControl<ListBox>("Folders")!;

    private static FolderEntry FolderOf(WorkingCopyViewModel view, string relPath) =>
        view.Folders.Single(folder => folder.Content.RelPath == relPath);

    private static string FolderPaths(WorkingCopyViewModel view) =>
        string.Join(",", view.Folders.Select(folder => folder.Content.RelPath));

    private static IEnumerable<string?> ItemNames(ListBox list) =>
        list.GetVisualDescendants()
            .OfType<ListBoxItem>()
            .Select(item => AutomationProperties.GetName(item));

    private static Button ChevronOf(ListBox folders, FolderEntry folder) =>
        folders
            .ContainerFromItem(folder)!
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.Name == "Chevron");

    private static void FocusFolder(ListBox folders, WorkingCopyViewModel view, string relPath)
    {
        var folder = FolderOf(view, relPath);
        view.SelectedFolder = folder;
        Dispatcher.UIThread.RunJobs();
        folders.ContainerFromItem(folder)!.Focus();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Window window, Visual target)
    {
        var centre = target
            .TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!
            .Value;
        window.MouseDown(centre, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(centre, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Focus(ListBox list, int index)
    {
        list.SelectedIndex = index;
        list.ContainerFromIndex(index)!.Focus();
        Dispatcher.UIThread.RunJobs();
    }

    private static Task<T> OnViewAsync<T>(
        Func<Window, ListBox, WorkingCopyViewModel, T> act,
        FakeFileLauncher? launcher = null,
        FakeWorkingCopyStatus? status = null
    ) =>
        HeadlessApp.Session.Dispatch(
            async () =>
            {
                status ??= new FakeWorkingCopyStatus().Answers(
                    // Unversioned, so they start unticked and a key's ticks are its own.
                    Listing(
                        Entry("a.png", NodeStatus.Unversioned),
                        Entry("b.png", NodeStatus.Unversioned),
                        Entry("sub/hero.png", NodeStatus.Unversioned)
                    )
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
