using Avalonia;
using Avalonia.Controls;
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

    private static FakeWorkingCopyStatus Studio() =>
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
        );

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
        FakeWorkingCopyCommit? commits = null
    )
    {
        Application.Current!.RequestedThemeVariant =
            variant == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var viewModel = new MainWindowViewModel(
            store,
            new FakeFolderPicker(null),
            path => WorkingCopies.View(status, path: path, commits: commits),
            new FakeTimeProvider(),
            StringComparison.Ordinal
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
