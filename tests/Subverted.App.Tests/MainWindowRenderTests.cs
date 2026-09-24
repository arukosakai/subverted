using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using Subverted.App.ViewModels;
using Subverted.App.Views;
using Subverted.Core;
using Subverted.Protocol;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>
/// The window as it really renders, headless. The assertions are about the visual tree the XAML
/// produced; the saved frames are for a person to look at, since nothing here has a display.
/// </summary>
public sealed class MainWindowRenderTests
{
    private static readonly string Screens = Path.Combine(AppContext.BaseDirectory, "screens");

    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    public async Task A_working_copy_renders_one_row_per_change(string variant)
    {
        var rows = await RenderAsync(
            variant,
            new FakeWorkingCopyStatus().Answers(
                Listing(
                    Entry("art/characters/hero.png"),
                    Entry("art/characters/villain.png", NodeStatus.Conflicted),
                    Entry("art/props/crate.png", NodeStatus.Added, isCopied: true),
                    Entry("levels/forest.map", NodeStatus.Modified, PropertyStatus.Modified),
                    Entry("levels/old-cave.map", NodeStatus.Deleted),
                    Entry("sound/theme.ogg", NodeStatus.Missing),
                    Entry("art/ui/button.psd", NodeStatus.Unmodified, hasLockToken: true),
                    Entry("notes.txt", NodeStatus.Unversioned)
                )
            ),
            $"working-copy-{variant.ToLowerInvariant()}.png"
        );

        await Assert.That(rows).IsEqualTo(8);
    }

    /// <summary>The tree nests the listing under folder lines, with the conflict and the missing file pinned above.</summary>
    [Test]
    public async Task The_tree_renders_a_line_per_change_and_per_folder()
    {
        var lines = 0;
        await RenderAsync(
            "Dark",
            Studio(),
            "working-copy-tree-dark.png",
            view =>
            {
                view.ShowTreeCommand.Execute(null);
                view.ToggleTickCommand.Execute(
                    view.Entries.Single(entry => entry.Key == "art/props/crate.png")
                );
                lines = view.Entries.Count;
            }
        );

        // 8 changes, 2 of them pinned; the other 6 hang under art/, characters/, props/, ui/ and
        // levels/. The list virtualises, so the lines are counted on the view model, not the screen.
        await Assert.That(lines).IsEqualTo(13);
    }

    /// <summary>art/ collapsed while art/characters was chosen: the choice climbs to art/, which keeps its count and tone.</summary>
    [Test]
    public async Task A_collapsed_folder_renders_without_the_lines_below_it()
    {
        var (lines, chosen) = ("", "");
        var rows = await RenderAsync(
            "Dark",
            Studio(),
            "folders-collapsed-dark.png",
            view =>
            {
                view.SelectedFolder = view.Folders.Single(folder =>
                    folder.Content.RelPath == "art/characters"
                );
                view.ToggleFolderCommand.Execute(
                    view.Folders.Single(folder => folder.Content.RelPath == "art")
                );
                lines = string.Join(
                    ",",
                    view.Folders.Select(folder =>
                        folder.Content.RelPath + ":" + folder.Content.Count
                    )
                );
                chosen = view.SelectedFolder!.Content.RelPath;
            }
        );

        await Assert.That(lines).IsEqualTo(":8,art:4,levels:2,sound:1");
        await Assert.That(chosen).IsEqualTo("art");
        await Assert.That(rows).IsEqualTo(4);
    }

    [Test]
    public async Task A_filter_renders_only_what_it_keeps()
    {
        var rows = await RenderAsync(
            "Dark",
            Studio(),
            "working-copy-filtered-dark.png",
            view => view.Filter = "art/"
        );

        await Assert.That(rows).IsEqualTo(4);
    }

    /// <summary>A commit stopped after marking: the notice says which step, SVN's text, and that a retry carries on.</summary>
    [Test]
    public async Task A_commit_left_marked_renders_its_notice_under_the_message()
    {
        var commits = new FakeWorkingCopyCommit().Answers(
            new SelectionNotCommittedResponse(
                new SelectionSchedule(["notes.txt"], ["sound/theme.ogg"], []),
                SelectionStep.Commit,
                "",
                "svn: E165001: Commit blocked by pre-commit hook (exit code 1) with output:\nRefused by the studio hook"
            )
        );
        var shown = false;
        await RenderAsync(
            "Dark",
            Studio(),
            "commit-left-marked-dark.png",
            view =>
            {
                view.Composer.Message = "Hero pass";
                view.Composer.CommitCommand.Execute(null);
                shown = view.Composer.Notice?.Kind == NoticeKind.LeftMarked;
            },
            commits
        );

        await Assert.That(shown).IsTrue();
    }

    [Test]
    public async Task The_revert_question_renders_over_the_list()
    {
        var asking = false;
        await RenderAsync(
            "Dark",
            Studio(),
            "revert-prompt-dark.png",
            view =>
            {
                view.RevertCommand.Execute(
                    view.Entries.Single(entry => entry.Key == "art/props/crate.png")
                );
                asking = view.RevertPrompt.IsAsking;
            }
        );

        await Assert.That(asking).IsTrue();
    }

    [Test]
    public async Task Taking_theirs_renders_its_question_over_the_list()
    {
        var asking = false;
        await RenderAsync(
            "Dark",
            Studio(),
            "resolve-prompt-dark.png",
            view =>
            {
                view.TakeTheirsCommand.Execute(
                    view.Entries.Single(entry => entry.Key == "art/characters/villain.png")
                );
                asking = view.Resolver.IsAsking;
            }
        );

        await Assert.That(asking).IsTrue();
    }

    [Test]
    public async Task An_update_that_left_conflicts_renders_its_notice_above_the_list()
    {
        var updates = new FakeWorkingCopyUpdate().Answers(
            new UpdateResponse(
                1826,
                1,
                0,
                "U    levels/forest.map\nC    art/characters/villain.png\nUpdated to revision 1826."
            )
        );
        var shown = await HeadlessApp.Session.Dispatch(
            async () =>
            {
                var window = await ShowAsync(
                    "Dark",
                    new FakeRecentStore("/studio/game"),
                    Studio(),
                    updates: updates
                );
                var button = window
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .Single(control => control.Name == "UpdateButton");
                button.Command!.Execute(null);
                for (var turn = 0; turn < 5; turn++)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Task.Yield();
                }

                Save(window, "update-conflicts-dark.png");
                var card = window
                    .GetVisualDescendants()
                    .OfType<NoticeView>()
                    .Single(control => control.Name == "UpdateNotice")
                    .FindControl<Border>("Card")!;
                var result = (button.IsEffectivelyVisible, card.IsEffectivelyVisible);
                window.Close();
                return result;
            },
            CancellationToken.None
        );

        await Assert.That(updates.Updated).IsEquivalentTo(new[] { "/studio/game" });
        await Assert.That(shown).IsEqualTo((true, true));
    }

    private static FakeWorkingCopyStatus Studio() =>
        new FakeWorkingCopyStatus().Answers(
            Listing(
                Entry("art/characters/hero.png"),
                Entry("art/characters/villain.png", NodeStatus.Conflicted, isConflicted: true),
                Entry("art/props/crate.png", NodeStatus.Added, isCopied: true),
                Entry("levels/forest.map", NodeStatus.Modified, PropertyStatus.Modified),
                Entry("levels/old-cave.map", NodeStatus.Deleted),
                Entry("sound/theme.ogg", NodeStatus.Missing),
                Entry("art/ui/button.psd", NodeStatus.Unmodified, hasLockToken: true),
                Entry("notes.txt", NodeStatus.Unversioned)
            )
        );

    /// <summary>The bottom strip is all commit box until a write has something to say.</summary>
    [Test]
    public async Task The_output_log_takes_half_the_strip_only_once_a_write_is_in_it()
    {
        var (before, after) = await HeadlessApp.Session.Dispatch(
            async () =>
            {
                var window = await ShowAsync(
                    "Dark",
                    new FakeRecentStore("/studio/game"),
                    Studio(),
                    new FakeWorkingCopyCommit().Answers(FakeWorkingCopyCommit.Committed(8))
                );
                var before = StripShares(window);

                var composer = ((MainWindowViewModel)window.DataContext!).Current!.Composer;
                composer.Message = "Hero pass";
                await composer.CommitCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Save(window, "output-log-after-commit.png");
                var after = StripShares(window);

                window.Close();
                return (before, after);
            },
            CancellationToken.None
        );

        await Assert.That(before).IsEqualTo((false, 1.0));
        await Assert.That(after).IsEqualTo((true, 0.5));
    }

    private static (bool LogShown, double ComposerShare) StripShares(MainWindow window)
    {
        var strip = window
            .GetVisualDescendants()
            .OfType<UniformGrid>()
            .Single(g => g.Name == "Strip");
        var composer = window
            .GetVisualDescendants()
            .OfType<CommitComposerView>()
            .Single(view => view.Name == "Composer");
        var log = window
            .GetVisualDescendants()
            .OfType<OutputLogView>()
            .Single(view => view.Name == "OutputLog");
        return (
            log.IsEffectivelyVisible,
            Math.Round(composer.Bounds.Width / strip.Bounds.Width, 2)
        );
    }

    /// <summary>With nothing listed, the tree, the diff, the commit box and the counts have nothing to say.</summary>
    [Test]
    public async Task A_clean_working_copy_shows_only_that_it_is_clean()
    {
        var clean = await PanesShownAsync(
            new FakeWorkingCopyStatus().Answers(Listing()),
            "clean-alone.png"
        );
        var changed = await PanesShownAsync(Studio(), "changed-panes.png");

        await Assert.That(clean).IsEqualTo((false, false, false, false));
        await Assert.That(changed).IsEqualTo((true, false, true, true));
    }

    /// <summary>The diff takes room only once a row is picked, and the table gives it back on a clear.</summary>
    [Test]
    public async Task Picking_a_row_opens_the_diff_beneath_the_table()
    {
        var (before, picked, cleared) = await HeadlessApp.Session.Dispatch(
            async () =>
            {
                var window = await ShowAsync("Dark", new FakeRecentStore("/studio/game"), Studio());
                var current = ((MainWindowViewModel)window.DataContext!).Current!;
                var list = Named<ListBox>(window, "List");
                var pane = window.GetVisualDescendants().OfType<DiffPaneView>().Single();
                (bool Shown, double Height) Look()
                {
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    return (pane.IsEffectivelyVisible, list.Bounds.Height);
                }

                var before = Look();
                current.Select("art/characters/hero.png");
                var picked = Look();
                Save(window, "diff-picked.png");
                current.SelectedEntry = null;
                var cleared = Look();
                window.Close();
                return (before, picked, cleared);
            },
            CancellationToken.None
        );

        await Assert.That(before.Shown).IsFalse();
        await Assert.That(picked.Shown).IsTrue();
        await Assert.That(picked.Height).IsLessThan(before.Height);
        await Assert.That(cleared).IsEqualTo(before);
    }

    /// <summary>Committing everything empties the listing, and what the commit said must not go with it.</summary>
    [Test]
    public async Task Committing_the_last_change_leaves_the_log_the_whole_strip()
    {
        var status = Studio();
        var commits = new FakeWorkingCopyCommit().Answers(FakeWorkingCopyCommit.Committed(8));
        var (logShown, composerShown, logShare) = await HeadlessApp.Session.Dispatch(
            async () =>
            {
                var window = await ShowAsync(
                    "Dark",
                    new FakeRecentStore("/studio/game"),
                    status,
                    commits
                );
                var current = ((MainWindowViewModel)window.DataContext!).Current!;
                current.Composer.Message = "Hero pass";
                await current.Composer.CommitCommand.ExecuteAsync(null);
                status.Answers(Listing());
                await current.RefreshAsync(CancellationToken.None);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Save(window, "output-log-alone.png");

                var strip = Named<UniformGrid>(window, "Strip");
                var log = Named<OutputLogView>(window, "OutputLog");
                var result = (
                    log.IsEffectivelyVisible,
                    Named<CommitComposerView>(window, "Composer").IsEffectivelyVisible,
                    Math.Round(log.Bounds.Width / strip.Bounds.Width, 2)
                );
                window.Close();
                return result;
            },
            CancellationToken.None
        );

        await Assert.That(commits.Commits.Single().Paths.Count).IsEqualTo(5);
        await Assert.That(logShown).IsTrue();
        await Assert.That(composerShown).IsFalse();
        await Assert.That(logShare).IsEqualTo(1.0);
    }

    [Test]
    public async Task An_update_on_a_clean_working_copy_still_shows_its_notice()
    {
        var updates = new FakeWorkingCopyUpdate().Answers(
            new UpdateResponse(1826, 0, 0, "Updated to revision 1826.")
        );
        var shown = await HeadlessApp.Session.Dispatch(
            async () =>
            {
                var window = await ShowAsync(
                    "Dark",
                    new FakeRecentStore("/studio/game"),
                    new FakeWorkingCopyStatus().Answers(Listing()),
                    updates: updates
                );
                Named<Button>(window, "UpdateButton").Command!.Execute(null);
                for (var turn = 0; turn < 5; turn++)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Task.Yield();
                }

                Save(window, "update-clean-dark.png");
                var card = Named<NoticeView>(window, "UpdateNotice").FindControl<Border>("Card")!;
                var result = card.IsEffectivelyVisible;
                window.Close();
                return result;
            },
            CancellationToken.None
        );

        await Assert.That(shown).IsTrue();
    }

    private static Task<(bool Tree, bool Diff, bool Composer, bool Counts)> PanesShownAsync(
        FakeWorkingCopyStatus status,
        string file
    ) =>
        HeadlessApp.Session.Dispatch(
            async () =>
            {
                var window = await ShowAsync("Dark", new FakeRecentStore("/studio/game"), status);
                Save(window, file);
                var result = (
                    Named<ListBox>(window, "Folders").IsEffectivelyVisible,
                    window
                        .GetVisualDescendants()
                        .OfType<DiffPaneView>()
                        .Single()
                        .IsEffectivelyVisible,
                    Named<CommitComposerView>(window, "Composer").IsEffectivelyVisible,
                    Named<Border>(window, "StatusLine").IsEffectivelyVisible
                );
                window.Close();
                return result;
            },
            CancellationToken.None
        );

    private static T Named<T>(MainWindow window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    [Test]
    public async Task A_clean_working_copy_renders_no_rows()
    {
        var rows = await RenderAsync(
            "Dark",
            new FakeWorkingCopyStatus().Answers(Listing()),
            "clean.png"
        );

        await Assert.That(rows).IsEqualTo(0);
    }

    [Test]
    public async Task No_daemon_renders_no_rows()
    {
        var rows = await RenderAsync(
            "Dark",
            new FakeWorkingCopyStatus().IsUnreachable("Nothing is listening on the daemon socket."),
            "unreachable.png"
        );

        await Assert.That(rows).IsEqualTo(0);
    }

    [Test]
    public async Task First_run_renders_without_a_working_copy()
    {
        var hasView = await HeadlessApp.Session.Dispatch(
            async () =>
            {
                var window = await ShowAsync(
                    "Dark",
                    new FakeRecentStore(),
                    new FakeWorkingCopyStatus()
                );
                Save(window, "first-run.png");
                var shown = window
                    .GetVisualDescendants()
                    .OfType<WorkingCopyView>()
                    .Any(view => view.IsEffectivelyVisible);
                window.Close();
                return shown;
            },
            CancellationToken.None
        );

        await Assert.That(hasView).IsFalse();
    }

    private static Task<int> RenderAsync(
        string variant,
        FakeWorkingCopyStatus status,
        string file,
        Action<WorkingCopyViewModel>? arrange = null,
        FakeWorkingCopyCommit? commits = null
    ) =>
        HeadlessApp.Session.Dispatch(
            async () =>
            {
                var window = await ShowAsync(
                    variant,
                    new FakeRecentStore("/studio/game", "/studio/tools", "/studio/website"),
                    status,
                    commits
                );
                if (arrange is not null)
                {
                    arrange(((MainWindowViewModel)window.DataContext!).Current!);
                    Dispatcher.UIThread.RunJobs();
                }

                Save(window, file);
                // The change table's own rows, not the directory tree's beside it.
                var rows = window
                    .GetVisualDescendants()
                    .OfType<ListBox>()
                    .Single(list => list.Name == "List")
                    .GetVisualDescendants()
                    .OfType<ListBoxItem>()
                    .Count();
                window.Close();
                return rows;
            },
            CancellationToken.None
        );

    private static async Task<MainWindow> ShowAsync(
        string variant,
        FakeRecentStore store,
        FakeWorkingCopyStatus status,
        FakeWorkingCopyCommit? commits = null,
        FakeWorkingCopyUpdate? updates = null
    )
    {
        Application.Current!.RequestedThemeVariant =
            variant == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var viewModel = new MainWindowViewModel(
            store,
            new FakeFolderPicker(null),
            path => WorkingCopies.View(status, path: path, commits: commits, updates: updates),
            new FakeTimeProvider(),
            StringComparison.Ordinal,
            Revisions.View()
        );

        var window = new MainWindow(viewModel);
        window.Show();

        // Opening starts the view model; the fake answers at once, so a few turns settle it.
        for (var turn = 0; turn < 5; turn++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }

        return window;
    }

    private static void Save(MainWindow window, string file)
    {
        Directory.CreateDirectory(Screens);
        window
            .CaptureRenderedFrame()
            ?.Save(Path.Combine(Screens, file), PngBitmapEncoderOptions.Default);
    }
}
