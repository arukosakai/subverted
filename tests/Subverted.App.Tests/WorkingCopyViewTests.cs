using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Subverted.App.Infrastructure;
using Subverted.App.Presentation;
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
                $"Open|{RevealMenuText.For(FileRevealers.ThisPlatform)}|Copy path|Revert…|Resolve|Lock|Unlock|History of this file"
            );
        await Assert.That(historyEnabled).IsFalse();
        await Assert.That(resolveHeaders).IsEqualTo("Keep mine|Take theirs…|Mark as resolved");
    }

    [Test]
    public async Task The_menu_offers_lock_and_unlock_by_what_the_picked_line_is()
    {
        var offered = await OnViewAsync(
            (_, list, view) =>
                string.Join(
                    ",",
                    new[] { "edited.png", "held.psd", "notes.txt" }.Select(key =>
                    {
                        Focus(list, IndexOf(view, key));
                        var (take, give) = LockItems(list);
                        var result = $"{key}:{Enabled(take)}/{Enabled(give)}";
                        list.ContextMenu!.Close();
                        return result;
                    })
                ),
            status: LockStatus()
        );

        await Assert.That(offered).IsEqualTo("edited.png:lock/-,held.psd:-/unlock,notes.txt:-/-");
    }

    /// <summary>A refusal is the one answer that must be read, so its SVN text is on screen, not only in the log.</summary>
    [Test]
    public async Task Clicking_lock_sends_the_file_and_a_refusal_shows_svn_s_text_above_the_list()
    {
        const string heldByRena =
            "svn: warning: W160035: Path '/edited.png' is already locked by user 'rena' in filesystem '/repo/db'";
        var locks = new FakeWorkingCopyLocks().Answers(
            new Subverted.Protocol.LockResponse("", [heldByRena])
        );

        var (cardShown, detail) = await OnViewAsync(
            (_, list, view) =>
            {
                Focus(list, IndexOf(view, "edited.png"));
                var (take, _) = LockItems(list);
                take.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                var notice = list.FindAncestorOfType<WorkingCopyView>()!
                    .FindControl<NoticeView>("LockNotice")!;
                return (
                    notice.FindControl<Border>("Card")!.IsEffectivelyVisible,
                    notice.FindControl<SelectableTextBlock>("Detail")!.Text
                );
            },
            status: LockStatus(),
            locks: locks
        );

        await Assert
            .That(locks.Locked)
            .IsEquivalentTo([DiffTarget.PathOf(Info.RootPath, "edited.png")]);
        await Assert.That(cardShown).IsTrue();
        await Assert.That(detail).IsEqualTo(heldByRena);
    }

    /// <summary>The toggle's reason to exist: an untouched file gets a line, and that line offers Lock.</summary>
    [Test]
    public async Task Clicking_all_lists_an_untouched_file_with_no_tick_box_and_lock_on_its_menu()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("edited.png")))
            .Answers(Listing(Entry("edited.png"), Entry("tree.png", NodeStatus.Unmodified)));

        var (before, after, ticks, folderIcons, lockOffered) = await OnViewAsync(
            (window, list, view) =>
            {
                var header = list.FindAncestorOfType<WorkingCopyView>()!;
                var all = header.FindControl<Button>("ListAll")!;
                var changes = header.FindControl<Button>("ListChanges")!;
                var activeBefore = Active(changes, all);
                Click(window, all);
                var activeAfter = Active(changes, all);
                var boxes = string.Join(",", VisibleOnLines<CheckBox>(list, view));
                var icons = string.Join(",", VisibleOnLines<Avalonia.Controls.Shapes.Path>(list, view));
                Focus(list, IndexOf(view, "tree.png"));
                var (take, _) = LockItems(list);
                var offered = Enabled(take);
                list.ContextMenu!.Close();
                return (activeBefore, activeAfter, boxes, icons, offered);
            },
            status: status
        );

        await Assert.That(before).IsEqualTo("changes");
        await Assert.That(after).IsEqualTo("all");
        await Assert.That(status.Listings).IsEquivalentTo([ListedNodes.Changes, ListedNodes.All]);
        await Assert.That(ticks).IsEqualTo("edited.png");
        await Assert.That(folderIcons).IsEqualTo("");
        await Assert.That(lockOffered).IsEqualTo("lock");
    }

    [Test]
    public async Task Clicking_changed_again_takes_the_untouched_file_s_line_away()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("edited.png")))
            .Answers(Listing(Entry("edited.png"), Entry("tree.png", NodeStatus.Unmodified)))
            .Answers(Listing(Entry("edited.png")));

        var (active, keys) = await OnViewAsync(
            (window, list, view) =>
            {
                var header = list.FindAncestorOfType<WorkingCopyView>()!;
                var all = header.FindControl<Button>("ListAll")!;
                var changes = header.FindControl<Button>("ListChanges")!;
                Click(window, all);
                Click(window, changes);
                return (Active(changes, all), string.Join(",", view.Entries.Select(e => e.Key)));
            },
            status: status
        );

        await Assert.That(active).IsEqualTo("changes");
        await Assert.That(keys).IsEqualTo("edited.png");
    }

    [Test]
    public async Task In_the_tree_a_folder_line_draws_its_icon_where_an_untouched_file_draws_nothing()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("art/hero.png")))
            .Answers(
                Listing(Entry("art/hero.png"), Entry("art/tree.png", NodeStatus.Unmodified))
            );

        var (ticks, folderIcons) = await OnViewAsync(
            (window, list, view) =>
            {
                Click(window, list.FindAncestorOfType<WorkingCopyView>()!.FindControl<Button>("ListAll")!);
                view.ShowTreeCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                return (
                    string.Join(",", VisibleOnLines<CheckBox>(list, view)),
                    string.Join(",", VisibleOnLines<Avalonia.Controls.Shapes.Path>(list, view))
                );
            },
            status: status
        );

        await Assert.That(ticks).IsEqualTo("art/hero.png");
        await Assert.That(folderIcons).IsEqualTo("art/");
    }

    /// <summary>Clean is where an artist locks before starting, so the message offers the full list.</summary>
    [Test]
    public async Task A_clean_copy_s_message_offers_every_file_and_clicking_it_lists_them()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing())
            .Answers(Listing(Entry("tree.png", NodeStatus.Unmodified)));

        var (messageBefore, offerBefore, messageAfter, shown) = await OnViewAsync(
            (window, list, _) =>
            {
                var view = list.FindAncestorOfType<WorkingCopyView>()!;
                var message = view.FindControl<StackPanel>("CleanMessage")!;
                var offer = view.FindControl<Button>("CleanListAll")!;
                var shownBefore = (message.IsEffectivelyVisible, offer.IsEffectivelyVisible);
                Click(window, offer);
                return (
                    shownBefore.Item1,
                    shownBefore.Item2,
                    message.IsEffectivelyVisible,
                    list.GetVisualDescendants().OfType<ListBoxItem>().Count()
                );
            },
            status: status
        );

        await Assert.That(messageBefore).IsTrue();
        await Assert.That(offerBefore).IsTrue();
        await Assert.That(messageAfter).IsFalse();
        await Assert.That(shown).IsEqualTo(1);
    }

    /// <summary>Already listing all, a copy with no files says it is clean and offers nothing more.</summary>
    [Test]
    public async Task A_copy_with_no_files_listing_all_keeps_its_message_without_the_offer()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing())
            .Answers(Listing(Entry("", NodeStatus.Unmodified, kind: NodeKind.Directory)));

        var (message, offer) = await OnViewAsync(
            (window, list, _) =>
            {
                var view = list.FindAncestorOfType<WorkingCopyView>()!;
                var offered = view.FindControl<Button>("CleanListAll")!;
                Click(window, offered);
                return (
                    view.FindControl<StackPanel>("CleanMessage")!.IsEffectivelyVisible,
                    offered.IsEffectivelyVisible
                );
            },
            status: status
        );

        await Assert.That(message).IsTrue();
        await Assert.That(offer).IsFalse();
    }

    /// <summary>A folder holding only untouched files is in the tree, but a zero is not a count worth drawing.</summary>
    [Test]
    public async Task Listing_all_a_folder_of_untouched_files_draws_no_count()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("art/hero.png")))
            .Answers(
                Listing(Entry("art/hero.png"), Entry("src/main.cs", NodeStatus.Unmodified))
            );

        var counts = await OnViewAsync(
            (window, list, _) =>
            {
                var view = list.FindAncestorOfType<WorkingCopyView>()!;
                Click(window, view.FindControl<Button>("ListAll")!);
                return string.Join(
                    "|",
                    view.FindControl<ListBox>("Folders")!
                        .GetVisualDescendants()
                        .OfType<ListBoxItem>()
                        .Select(item =>
                            item.GetVisualDescendants()
                                .OfType<TextBlock>()
                                .Single(text => text.Name == "FolderCount")
                        )
                        .Select(count => count.IsEffectivelyVisible ? count.Text : "-")
                );
            },
            status: status
        );

        await Assert.That(counts).IsEqualTo("1|1|-");
    }

    private static string Active(Button changes, Button all) =>
        (changes.Classes.Contains("active"), all.Classes.Contains("active")) switch
        {
            (true, false) => "changes",
            (false, true) => "all",
            var both => $"changes={both.Item1}, all={both.Item2}",
        };

    /// <summary>The keys of the lines whose <typeparamref name="T"/> in the line's leading column is drawn.</summary>
    private static IEnumerable<string> VisibleOnLines<T>(ListBox list, WorkingCopyViewModel view)
        where T : Control =>
        view.Entries.Where(entry =>
                list.ContainerFromItem(entry)!
                    .GetVisualDescendants()
                    .OfType<T>()
                    .Any(control =>
                        control.GetVisualParent() is Grid { ColumnDefinitions.Count: 5 } line
                        && Grid.GetColumn(control) == 0
                        && control.IsEffectivelyVisible
                    )
            )
            .Select(entry => entry.Key);

    private static FakeWorkingCopyStatus LockStatus() =>
        new FakeWorkingCopyStatus().Answers(
            Listing(
                Entry("edited.png"),
                Entry("held.psd", NodeStatus.Unmodified, hasLockToken: true),
                Entry("notes.txt", NodeStatus.Unversioned)
            )
        );

    private static int IndexOf(WorkingCopyViewModel view, string key) =>
        view.Entries.IndexOf(view.Entries.Single(entry => entry.Key == key));

    /// <summary>Opens the list's menu and hands back its Lock and Unlock items.</summary>
    private static (MenuItem Lock, MenuItem Unlock) LockItems(ListBox list)
    {
        var menu = list.ContextMenu!;
        menu.Open(list);
        Dispatcher.UIThread.RunJobs();
        var items = menu.Items.OfType<MenuItem>().ToList();
        return (
            items.Single(item => Equals(item.Header, "Lock")),
            items.Single(item => Equals(item.Header, "Unlock"))
        );
    }

    private static string Enabled(MenuItem item) =>
        item.IsEffectivelyEnabled ? ((string)item.Header!).ToLowerInvariant() : "-";

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
        FakeWorkingCopyStatus? status = null,
        FakeWorkingCopyLocks? locks = null
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
                var view = WorkingCopies.View(status, launcher: launcher, locks: locks);
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
