using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Subverted.App.Presentation;
using Subverted.App.ViewModels;
using Subverted.App.Views;
using Subverted.Frontend.Diff;
using TUnit.Assertions.Enums;

namespace Subverted.App.Tests;

/// <summary>
/// The diff viewer as it really renders, headless, around hand-built documents shaped like SVN's.
/// The assertions read the visual tree; the saved frames under <c>screens/</c> are for a person.
/// </summary>
public sealed class DiffViewRenderTests
{
    private static readonly string Screens = Path.Combine(AppContext.BaseDirectory, "screens");

    private static readonly Dictionary<string, DiffDocument> Documents = new()
    {
        ["modified"] = Diffs.Modified,
        ["added"] = Diffs.AddedFile,
        ["deleted"] = Diffs.DeletedFile,
        ["no-newline"] = Diffs.NoNewlineAtEnd,
        ["properties-only"] = Diffs.PropertiesOnly,
        ["content-and-properties"] = Diffs.ContentAndProperties,
        ["binary"] = Diffs.Binary,
        ["directory"] = Diffs.WholeDirectory,
        ["long-line"] = Diffs.LongLine,
    };

    /// <summary>
    /// Every row realised, and none of them drawn as a record's <c>ToString</c> — which is what
    /// Avalonia falls back to when no data template matches a row's type.
    /// </summary>
    [Test]
    [MatrixDataSource]
    public async Task Every_row_renders_through_a_template_of_its_own(
        [Matrix(
            "modified",
            "added",
            "deleted",
            "no-newline",
            "properties-only",
            "content-and-properties",
            "binary",
            "directory",
            "long-line"
        )]
            string name,
        [Matrix("Dark", "Light")] string variant
    )
    {
        var document = Documents[name];
        var (realised, fallbacks) = await RenderAsync(
            variant,
            () =>
                new DiffLinesView
                {
                    Document = document,
                    SizeInBytes = 5 * 1024 * 1024 + 300 * 1024,
                },
            $"diff-{name}-{variant.ToLowerInvariant()}.png",
            window =>
                (
                    window.GetVisualDescendants().OfType<ListBoxItem>().Count(),
                    TextsOf(window).Count(text => text.Contains(" { ", StringComparison.Ordinal))
                )
        );

        await Assert.That(realised).IsEqualTo(DiffRows.Of(document).Count);
        await Assert.That(fallbacks).IsEqualTo(0);
    }

    [Test]
    public async Task A_lone_binary_file_offers_its_size_and_open_in_app()
    {
        var opened = 0;
        var (texts, button) = await RenderAsync(
            "Dark",
            () =>
                new DiffLinesView
                {
                    Document = Diffs.Binary,
                    SizeInBytes = 5 * 1024 * 1024 + 300 * 1024,
                    OpenInAppCommand = new Counting(() => opened++),
                },
            "diff-binary-card.png",
            window =>
            {
                var open = window
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .Single(b => b.IsEffectivelyVisible);
                open.Command!.Execute(open.CommandParameter);
                return (VisibleTextsOf(window), open);
            }
        );

        await Assert.That(texts).Contains("Binary file");
        await Assert.That(texts).Contains("image/png");
        await Assert.That(texts).Contains("5.3 MB");
        await Assert.That(button).IsNotNull();
        await Assert.That(opened).IsEqualTo(1);
    }

    [Test]
    public async Task A_lone_binary_file_that_is_not_on_disk_shows_no_size()
    {
        var texts = await RenderAsync(
            "Dark",
            () =>
                new DiffLinesView
                {
                    Document = Diffs.Binary,
                    SizeInBytes = null,
                    OpenInAppCommand = new Counting(() => { }),
                },
            "diff-binary-no-size.png",
            VisibleTextsOf
        );

        await Assert.That(texts).Contains("Open in app");
        await Assert
            .That(texts.Any(text => text.EndsWith("MB", StringComparison.Ordinal)))
            .IsFalse();
    }

    /// <summary>A committed revision's binary has no file on disk, so the History pane gives no command.</summary>
    [Test]
    public async Task A_binary_card_given_nothing_to_open_offers_no_open_in_app()
    {
        var texts = await RenderAsync(
            "Dark",
            () => new DiffLinesView { Document = Diffs.Binary, SizeInBytes = null },
            "diff-binary-history.png",
            VisibleTextsOf
        );

        await Assert.That(texts).Contains("Binary file");
        await Assert.That(texts).DoesNotContain("Open in app");
    }

    /// <summary>The pane's size and command are the directory's, so a file inside it gets neither.</summary>
    [Test]
    public async Task A_binary_file_inside_a_directory_diff_offers_neither_size_nor_open_in_app()
    {
        var texts = await RenderAsync(
            "Dark",
            () => new DiffLinesView { Document = Diffs.WholeDirectory, SizeInBytes = 4096 },
            "diff-directory-binary.png",
            VisibleTextsOf
        );

        await Assert.That(texts).Contains("Binary file");
        await Assert.That(texts).Contains("No MIME type recorded");
        await Assert.That(texts).DoesNotContain("4 KB");
        await Assert.That(texts).DoesNotContain("Open in app");
    }

    [Test]
    [Arguments("no-newline", 2)]
    [Arguments("modified", 0)]
    public async Task A_line_without_a_final_newline_is_marked(string name, int marked)
    {
        var texts = await RenderAsync(
            "Dark",
            () => new DiffLinesView { Document = Documents[name] },
            $"diff-marker-{name}.png",
            VisibleTextsOf
        );

        await Assert.That(texts.Count(text => text == "No newline at end")).IsEqualTo(marked);
    }

    [Test]
    public async Task A_long_line_scrolls_sideways_rather_than_wrapping()
    {
        var (extent, viewport, lineHeights) = await RenderAsync(
            "Dark",
            () => new DiffLinesView { Document = Diffs.LongLine },
            "diff-long-line-scroll.png",
            window =>
            {
                var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().First();
                var heights = window
                    .GetVisualDescendants()
                    .OfType<Grid>()
                    .Where(grid => grid.Classes.Contains("line"))
                    .Select(grid => grid.Bounds.Height)
                    .ToList();
                return (scroller.Extent.Width, scroller.Viewport.Width, heights);
            }
        );

        await Assert.That(extent).IsGreaterThan(viewport * 2);
        await Assert.That(lineHeights).IsEquivalentTo([20.0, 20.0], CollectionOrdering.Matching);
    }

    /// <summary>
    /// Only what is on screen gets a container, at the top and after jumping to the end — and the
    /// timings are printed, so a regression in either shows up in the test output.
    /// </summary>
    [Test]
    public async Task A_fifty_thousand_line_diff_realises_only_the_rows_on_screen()
    {
        var document = Diffs.Huge(500);
        var rowCount = DiffRows.Of(document).Count;

        var (atTop, atEnd, lastShown) = await RenderAsync(
            "Dark",
            () => new DiffLinesView { Document = document },
            "diff-huge-top.png",
            window =>
            {
                var list = window.GetVisualDescendants().OfType<ListBox>().Single();
                var top = window.GetVisualDescendants().OfType<ListBoxItem>().Count();

                var clock = Stopwatch.StartNew();
                list.ScrollIntoView(rowCount - 1);
                Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame();
                Console.WriteLine(
                    $"Jump to row {rowCount} and render: {clock.ElapsedMilliseconds} ms"
                );

                var realised = window.GetVisualDescendants().OfType<ListBoxItem>().ToList();
                var last = realised.Any(item =>
                    item.DataContext is DiffTextRow { Line.NewNumber: 49_975 }
                );
                return (top, realised.Count, last);
            }
        );

        await Assert.That(rowCount).IsEqualTo(50_500);
        await Assert.That(atTop).IsLessThan(60);
        await Assert.That(atEnd).IsLessThan(60);
        await Assert.That(lastShown).IsTrue();
    }

    [Test]
    public async Task Copying_selected_lines_puts_their_text_on_the_clipboard()
    {
        var copied = await HeadlessApp.Session.Dispatch(
            async () =>
            {
                var view = new DiffLinesView { Document = Diffs.Modified };
                var window = Show("Dark", view);
                var list = window.GetVisualDescendants().OfType<ListBox>().Single();
                list.Selection.Select(5);
                list.Selection.Select(4);
                list.ContainerFromIndex(4)!.Focus();
                window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
                Dispatcher.UIThread.RunJobs();
                var text = await window.Clipboard!.TryGetTextAsync();
                window.Close();
                return text;
            },
            CancellationToken.None
        );

        await Assert
            .That(copied)
            .IsEqualTo(
                string.Join(
                    Environment.NewLine,
                    "        position += velocity;",
                    "        position += velocity * delta;"
                )
            );
    }

    [Test]
    public async Task A_key_that_is_not_copy_leaves_the_clipboard_alone()
    {
        var clipboard = await HeadlessApp.Session.Dispatch(
            async () =>
            {
                var window = Show("Dark", new DiffLinesView { Document = Diffs.Modified });
                var list = window.GetVisualDescendants().OfType<ListBox>().Single();
                await window.Clipboard!.SetTextAsync("what was there before");
                list.Selection.Select(4);
                list.ContainerFromIndex(4)!.Focus();
                window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                var text = await window.Clipboard!.TryGetTextAsync();
                window.Close();
                return text;
            },
            CancellationToken.None
        );

        await Assert.That(clipboard).IsEqualTo("what was there before");
    }

    /// <summary>
    /// Only the default state: the pane's other states are set by its view model's own behaviour,
    /// which has no public way in from a test of the view.
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    public async Task The_diff_pane_with_nothing_selected_asks_for_a_selection(string variant)
    {
        var texts = await RenderAsync(
            variant,
            () => new DiffPaneView { DataContext = DiffPanes.Pane() },
            $"diff-pane-nothing-selected-{variant.ToLowerInvariant()}.png",
            VisibleTextsOf,
            unframed: true
        );

        await Assert
            .That(texts)
            .IsEquivalentTo(["Select a change to see its diff"], CollectionOrdering.Matching);
    }

    private static Task<T> RenderAsync<T>(
        string variant,
        Func<Control> content,
        string file,
        Func<Window, T> inspect,
        bool unframed = false
    ) =>
        HeadlessApp.Session.Dispatch(
            () =>
            {
                var window = Show(variant, unframed ? content() : Framed(content()));
                Directory.CreateDirectory(Screens);
                window
                    .CaptureRenderedFrame()
                    ?.Save(Path.Combine(Screens, file), PngBitmapEncoderOptions.Default);
                var result = inspect(window);
                window.Close();
                return Task.FromResult(result);
            },
            CancellationToken.None
        );

    private static Window Show(string variant, Control content)
    {
        var application = Application.Current!;
        application.RequestedThemeVariant =
            variant == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        application.TryGetResource(
            "WindowFallbackBrush",
            application.ActualThemeVariant,
            out var background
        );

        var window = new Window
        {
            Width = 900,
            Height = 760,
            Background = (IBrush?)background,
            Content = new Border { Padding = new Thickness(16), Child = content },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>The pane's glass panel around the lines, so the frame shows them as the app does.</summary>
    private static Border Framed(Control content)
    {
        var panel = new Border { Padding = new Thickness(8), Child = content };
        panel.Classes.Add("panel");
        return panel;
    }

    private static IEnumerable<string> TextsOf(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text ?? string.Empty);

    private static List<string> VisibleTextsOf(Window window) =>
        window
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text))
            .Select(text => text.Text!)
            .ToList();

    private sealed class Counting(Action onExecute) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => onExecute();
    }
}
