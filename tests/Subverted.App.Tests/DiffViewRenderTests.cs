using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Subverted.App.Presentation;
using Subverted.App.ViewModels;
using Subverted.App.Views;
using Subverted.Core;
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

    private static readonly Dictionary<string, DiffLayout> Layouts = new()
    {
        ["Split"] = DiffLayout.Split,
        ["Unified"] = DiffLayout.Unified,
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
        [Matrix("Dark", "Light")] string variant,
        [Matrix("Split", "Unified")] string layoutName
    )
    {
        var document = Documents[name];
        var layout = Layouts[layoutName];
        var (realised, fallbacks) = await RenderAsync(
            variant,
            () =>
                new DiffLinesView
                {
                    Document = document,
                    Subject = Diffs.SubjectOf(document),
                    Layout = layout,
                    SizeInBytes = 5 * 1024 * 1024 + 300 * 1024,
                },
            $"diff-{layoutName.ToLowerInvariant()}-{name}-{variant.ToLowerInvariant()}.png",
            window =>
                (
                    window.GetVisualDescendants().OfType<ListBoxItem>().Count(),
                    TextsOf(window).Count(text => text.Contains(" { ", StringComparison.Ordinal))
                )
        );

        await Assert
            .That(realised)
            .IsEqualTo(layout.RowsOf(document, Diffs.SubjectOf(document)).Count);
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
                    Subject = Diffs.SubjectOf(Diffs.Binary),
                    SizeInBytes = 5 * 1024 * 1024 + 300 * 1024,
                    OpenInAppCommand = new Counting(() => opened++),
                },
            "diff-binary-card.png",
            window =>
            {
                var open = window
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .Single(b => b.IsEffectivelyVisible && b.Classes.Contains("accent"));
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
                    Subject = Diffs.SubjectOf(Diffs.Binary),
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
            () =>
                new DiffLinesView
                {
                    Document = Diffs.Binary,
                    Subject = Diffs.SubjectOf(Diffs.Binary),
                    SizeInBytes = null,
                },
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
            () =>
                new DiffLinesView
                {
                    Document = Diffs.WholeDirectory,
                    Subject = Diffs.SubjectOf(Diffs.WholeDirectory),
                    SizeInBytes = 4096,
                },
            "diff-directory-binary.png",
            VisibleTextsOf
        );

        await Assert.That(texts).Contains("Binary file");
        await Assert.That(texts).Contains("No MIME type recorded");
        await Assert.That(texts).DoesNotContain("4 KB");
        await Assert.That(texts).DoesNotContain("Open in app");
    }

    [Test]
    [Arguments("no-newline", "Split", 2)]
    [Arguments("no-newline", "Unified", 2)]
    [Arguments("modified", "Split", 0)]
    [Arguments("modified", "Unified", 0)]
    [Arguments("removed-last-line", "Split", 1)]
    [Arguments("removed-last-line", "Unified", 1)]
    public async Task A_line_without_a_final_newline_is_marked(
        string name,
        string layoutName,
        int marked
    )
    {
        var document =
            name == "removed-last-line"
                ? Diffs.Document(
                    Diffs.Text(
                        "notes.txt",
                        new Hunk(
                            1,
                            2,
                            1,
                            1,
                            [
                                Diffs.Context(1, 1, "kept"),
                                Diffs.Removed(2, "gone", endsWithoutNewline: true),
                            ]
                        )
                    )
                )
                : Documents[name];
        var texts = await RenderAsync(
            "Dark",
            () =>
                new DiffLinesView
                {
                    Document = document,
                    Subject = Diffs.SubjectOf(document),
                    Layout = Layouts[layoutName],
                },
            $"diff-marker-{name}-{layoutName.ToLowerInvariant()}.png",
            VisibleTextsOf
        );

        await Assert.That(texts.Count(text => text == "No newline at end")).IsEqualTo(marked);
    }

    [Test]
    public async Task A_long_line_in_one_column_scrolls_sideways_rather_than_wrapping()
    {
        var (extent, viewport, lineHeights) = await RenderAsync(
            "Dark",
            () =>
                new DiffLinesView
                {
                    Document = Diffs.LongLine,
                    Subject = Diffs.SubjectOf(Diffs.LongLine),
                    Layout = DiffLayout.Unified,
                },
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
    [Arguments("Unified", 50_500)]
    [Arguments("Split", 38_000)]
    public async Task A_fifty_thousand_line_diff_realises_only_the_rows_on_screen(
        string layoutName,
        int expectedRows
    )
    {
        var document = Diffs.Huge(500);
        var layout = Layouts[layoutName];
        var rowCount = layout.RowsOf(document, Diffs.SubjectOf(document)).Count;

        var (atTop, atEnd, lastShown) = await RenderAsync(
            "Dark",
            () =>
                new DiffLinesView
                {
                    Document = document,
                    Subject = Diffs.SubjectOf(document),
                    Layout = layout,
                },
            $"diff-huge-top-{layoutName.ToLowerInvariant()}.png",
            window =>
            {
                var list = window.GetVisualDescendants().OfType<ListBox>().Single();
                var top = window.GetVisualDescendants().OfType<ListBoxItem>().Count();

                var clock = Stopwatch.StartNew();
                list.ScrollIntoView(rowCount - 1);
                Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame();
                Console.WriteLine(
                    $"{layoutName}: jump to row {rowCount} and render: {clock.ElapsedMilliseconds} ms"
                );

                var realised = window.GetVisualDescendants().OfType<ListBoxItem>().ToList();
                var last = realised.Any(item =>
                    item.DataContext
                        is DiffTextRow { Line.NewNumber: 49_975 }
                            or DiffSplitRow { New.NewNumber: 49_975 }
                );
                return (top, realised.Count, last);
            }
        );

        await Assert.That(rowCount).IsEqualTo(expectedRows);
        await Assert.That(atTop).IsLessThan(60);
        await Assert.That(atEnd).IsLessThan(60);
        await Assert.That(lastShown).IsTrue();
    }

    /// <summary>
    /// Read back from the frame: under the changed digit the wash is stronger than under the
    /// unchanged space beside it, in the removed line's tone on one side and the added's on the other.
    /// </summary>
    [Test]
    [MatrixDataSource]
    public async Task The_characters_that_changed_are_painted_stronger_than_the_rest_of_the_line(
        [Matrix("Dark", "Light")] string variant,
        [Matrix("Split", "Unified")] string layoutName
    )
    {
        var seen = await RenderAsync(
            variant,
            () =>
                new DiffLinesView
                {
                    Document = Diffs.Document(Diffs.Text("src/Player.cs", Diffs.OneLineHunk)),
                    Subject = "src/Player.cs",
                    Layout = Layouts[layoutName],
                },
            $"diff-intraline-{layoutName.ToLowerInvariant()}-{variant.ToLowerInvariant()}.png",
            window =>
            {
                using var frame = window.CaptureRenderedFrame()!;
                return window
                    .GetVisualDescendants()
                    .OfType<IntralineTextBlock>()
                    .Select(block =>
                    {
                        var span = block.Changes.Single();
                        var bounds = block
                            .TextLayout.HitTestTextRange(span.Start, span.Length)
                            .Single();
                        var origin = block.TranslatePoint(bounds.TopLeft, window)!.Value;
                        var y = origin.Y + bounds.Height - 2;
                        return (
                            block.Text,
                            Span: span,
                            IsRemovedTone: ReferenceEquals(
                                block.ChangeBrush,
                                ToneOf(block, "Diff.Removed.Word")
                            ),
                            IsAddedTone: ReferenceEquals(
                                block.ChangeBrush,
                                ToneOf(block, "Diff.Added.Word")
                            ),
                            Changed: PixelAt(frame, origin.X + bounds.Width / 2, y),
                            Unchanged: PixelAt(frame, origin.X - bounds.Width / 2, y)
                        );
                    })
                    .ToList();
            }
        );

        await Assert.That(seen.Count).IsEqualTo(2);
        await Assert.That(seen[0].Text).IsEqualTo("    const int MaxJumps = 1;");
        await Assert.That(seen[0].Span).IsEqualTo(new ChangedSpan(25, 1));
        await Assert.That((seen[0].IsRemovedTone, seen[0].IsAddedTone)).IsEqualTo((true, false));
        await Assert.That(seen[1].Text).IsEqualTo("    const int MaxJumps = 2;");
        await Assert.That(seen[1].Span).IsEqualTo(new ChangedSpan(25, 1));
        await Assert.That((seen[1].IsRemovedTone, seen[1].IsAddedTone)).IsEqualTo((false, true));
        await Assert.That(seen[0].Changed).IsNotEqualTo(seen[0].Unchanged);
        await Assert.That(seen[1].Changed).IsNotEqualTo(seen[1].Unchanged);
    }

    /// <summary>The spans are painted in the brush given; without one the text block marks nothing.</summary>
    [Test]
    [Arguments(true, true)]
    [Arguments(false, false)]
    public async Task A_text_block_paints_its_changed_spans_only_when_given_a_brush(
        bool hasBrush,
        bool painted
    )
    {
        var seen = await RenderAsync(
            "Dark",
            () =>
                new IntralineTextBlock
                {
                    Text = "count = 22",
                    Changes = [new ChangedSpan(8, 2)],
                    ChangeBrush = hasBrush ? Brushes.Yellow : null,
                },
            $"diff-intraline-block-{(hasBrush ? "brush" : "no-brush")}.png",
            window =>
            {
                using var frame = window.CaptureRenderedFrame()!;
                var block = window.GetVisualDescendants().OfType<IntralineTextBlock>().Single();
                var bounds = block.TextLayout.HitTestTextRange(8, 2).Single();
                var origin = block.TranslatePoint(bounds.TopLeft, window)!.Value;
                var y = origin.Y + bounds.Height - 2;
                return (
                    Changed: PixelAt(frame, origin.X + bounds.Width / 2, y),
                    Unchanged: PixelAt(frame, origin.X - bounds.Width / 4, y)
                );
            }
        );

        await Assert.That(seen.Changed != seen.Unchanged).IsEqualTo(painted);
    }

    /// <summary>Only a line with a partner is marked; context has no tone to mark in at all.</summary>
    [Test]
    [Arguments("Split")]
    [Arguments("Unified")]
    public async Task Only_paired_changed_lines_carry_changed_spans(string layoutName)
    {
        var seen = await RenderAsync(
            "Dark",
            () =>
                new DiffLinesView
                {
                    Document = Diffs.Modified,
                    Subject = Diffs.SubjectOf(Diffs.Modified),
                    Layout = Layouts[layoutName],
                },
            $"diff-intraline-modified-{layoutName.ToLowerInvariant()}.png",
            window =>
                window
                    .GetVisualDescendants()
                    .OfType<IntralineTextBlock>()
                    .Where(block => block.Text is not null)
                    .DistinctBy(block => block.Text)
                    .ToDictionary(
                        block => block.Text!,
                        block => (block.Changes.ToList(), block.ChangeBrush is not null)
                    )
        );

        await Assert
            .That(seen["        position += velocity * delta;"].Item1)
            .IsEquivalentTo([new ChangedSpan(28, 8)], CollectionOrdering.Matching);
        await Assert.That(seen["        position += velocity * delta;"].Item2).IsTrue();
        await Assert.That(seen["        position += velocity;"].Item1).IsEmpty();
        await Assert.That(seen["        ClampToLevel();"].Item1).IsEmpty();
        await Assert.That(seen["    {"].Item1).IsEmpty();
        await Assert.That(seen["    {"].Item2).IsFalse();
    }

    [Test]
    public async Task Copying_selected_lines_puts_their_text_on_the_clipboard()
    {
        var copied = await HeadlessApp.Session.Dispatch(
            async () =>
            {
                var view = new DiffLinesView
                {
                    Document = Diffs.Modified,
                    Subject = Diffs.SubjectOf(Diffs.Modified),
                    Layout = DiffLayout.Unified,
                };
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
                var window = Show(
                    "Dark",
                    new DiffLinesView
                    {
                        Document = Diffs.Modified,
                        Subject = Diffs.SubjectOf(Diffs.Modified),
                        Layout = DiffLayout.Unified,
                    }
                );
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

    [Test]
    public async Task A_new_viewer_lays_the_diff_out_side_by_side()
    {
        var layout = await RenderAsync(
            "Dark",
            () =>
                new DiffLinesView
                {
                    Document = Diffs.Modified,
                    Subject = Diffs.SubjectOf(Diffs.Modified),
                },
            "diff-default-layout.png",
            window => window.GetVisualDescendants().OfType<DiffLinesView>().Single().Layout
        );

        await Assert.That(layout).IsSameReferenceAs(DiffLayout.Split);
    }

    /// <summary>
    /// Every half of every row starts and ends where the others do, so a line reads straight across
    /// to its partner; the two halves are equal; and a hunk header spans both.
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    public async Task Side_by_side_rows_line_up_old_beside_new(string variant)
    {
        var seen = await RenderAsync(
            variant,
            () =>
                new DiffLinesView
                {
                    Document = Diffs.Modified,
                    Subject = Diffs.SubjectOf(Diffs.Modified),
                    OldTitle = "BASE",
                    NewTitle = "Working copy",
                },
            $"diff-split-aligned-{variant.ToLowerInvariant()}.png",
            window =>
            {
                var items = window.GetVisualDescendants().OfType<ListBoxItem>().ToList();
                var halves = items
                    .Where(item => item.DataContext is DiffSplitRow)
                    .Select(item =>
                    {
                        var sides = item.GetVisualDescendants()
                            .OfType<Grid>()
                            .Where(grid => grid.Classes.Contains("line"))
                            .Select(grid => grid.Bounds)
                            .ToList();
                        return (Left: sides[0], Right: sides[1]);
                    })
                    .ToList();
                var hunkWidths = items
                    .Where(item => item.DataContext is DiffHunkRow)
                    .Select(item => item.Bounds.Width)
                    .ToList();
                var rowTexts = items
                    .Where(item => item.DataContext is DiffSplitRow)
                    .Select(item =>
                        string.Join(
                            " | ",
                            item.GetVisualDescendants()
                                .OfType<TextBlock>()
                                .Where(text => text.IsEffectivelyVisible)
                                .Select(text => text.Text ?? string.Empty)
                        )
                    )
                    .ToList();
                var fillers = window
                    .GetVisualDescendants()
                    .OfType<Grid>()
                    .Count(grid => grid.Classes.Contains("filler"));
                return (
                    halves,
                    hunkWidths,
                    rowTexts,
                    fillers,
                    VisibleTexts: VisibleTextsOf(window)
                );
            }
        );

        var first = seen.halves[0];
        await Assert.That(seen.halves.Count).IsEqualTo(9);
        await Assert
            .That(seen.halves.All(half => half.Left == first.Left && half.Right == first.Right))
            .IsTrue();
        await Assert.That(first.Left.Width).IsEqualTo(first.Right.Width).Within(1.0);
        await Assert.That(first.Right.X).IsEqualTo(first.Left.Right + 1).Within(0.5);
        await Assert.That(first.Left.Height).IsEqualTo(20.0);
        await Assert.That(seen.hunkWidths.All(width => width >= first.Right.Right)).IsTrue();
        await Assert.That(seen.fillers).IsEqualTo(1);
        await Assert
            .That(seen.rowTexts.Take(6))
            .IsEquivalentTo(
                [
                    "10 |  |     public void Update(float delta) | 10 |  |     public void Update(float delta)",
                    "11 |  |     { | 11 |  |     {",
                    "12 |  |         velocity += gravity * delta; | 12 |  |         velocity += gravity * delta;",
                    "13 | − |         position += velocity; | 13 | + |         position += velocity * delta;",
                    " |  |  | 14 | + |         ClampToLevel();",
                    "14 |  |     } | 15 |  |     }",
                ],
                CollectionOrdering.Matching
            );
        await Assert.That(seen.VisibleTexts).Contains("BASE");
        await Assert.That(seen.VisibleTexts).Contains("Working copy");
    }

    [Test]
    public async Task A_long_line_side_by_side_is_cut_off_rather_than_scrolled_or_wrapped()
    {
        var (extent, viewport, lineHeights, trimmed) = await RenderAsync(
            "Dark",
            () =>
                new DiffLinesView
                {
                    Document = Diffs.LongLine,
                    Subject = Diffs.SubjectOf(Diffs.LongLine),
                },
            "diff-split-long-line.png",
            window =>
            {
                var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().First();
                var heights = window
                    .GetVisualDescendants()
                    .OfType<Grid>()
                    .Where(grid => grid.Classes.Contains("line"))
                    .Select(grid => grid.Bounds.Height)
                    .ToList();
                var cut = window
                    .GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Count(text =>
                        text.TextTrimming == TextTrimming.CharacterEllipsis
                        && text.Text?.StartsWith('[') == true
                    );
                return (scroller.Extent.Width, scroller.Viewport.Width, heights, cut);
            }
        );

        await Assert.That(extent).IsLessThanOrEqualTo(viewport);
        await Assert.That(lineHeights).IsEquivalentTo([20.0, 20.0], CollectionOrdering.Matching);
        await Assert.That(trimmed).IsEqualTo(2);
    }

    /// <summary>The column names are the split layout's; one column has nothing to name.</summary>
    [Test]
    public async Task The_layout_buttons_switch_between_side_by_side_and_one_column()
    {
        var seen = await RenderAsync(
            "Dark",
            () =>
                new DiffLinesView
                {
                    Document = Diffs.Modified,
                    Subject = Diffs.SubjectOf(Diffs.Modified),
                },
            "diff-layout-toggle.png",
            window =>
            {
                var view = window.GetVisualDescendants().OfType<DiffLinesView>().Single();
                var buttons = window
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .Where(button => button.Classes.Contains("layout"))
                    .ToDictionary(button => (string)button.Content!);
                var list = window.GetVisualDescendants().OfType<ListBox>().Single();

                IReadOnlyList<DiffRow> RowsShown() => (IReadOnlyList<DiffRow>)list.ItemsSource!;
                bool Active(string name) => buttons[name].Classes.Contains("active");
                (IReadOnlyList<DiffRow>, bool, bool, bool) Look()
                {
                    Dispatcher.UIThread.RunJobs();
                    return (
                        RowsShown(),
                        Active("Split"),
                        Active("Unified"),
                        VisibleTextsOf(window).Contains("Before")
                    );
                }

                var atFirst = Look();
                buttons["Unified"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var unified = Look();
                window
                    .CaptureRenderedFrame()
                    ?.Save(
                        Path.Combine(Screens, "diff-layout-unified.png"),
                        PngBitmapEncoderOptions.Default
                    );
                buttons["Split"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var splitAgain = Look();
                return (atFirst, unified, splitAgain, view.Layout);
            }
        );

        await Assert
            .That(seen.atFirst.Item1)
            .IsEquivalentTo(
                DiffLayout.Split.RowsOf(Diffs.Modified, "src/Player.cs"),
                CollectionOrdering.Matching
            );
        await Assert
            .That((seen.atFirst.Item2, seen.atFirst.Item3, seen.atFirst.Item4))
            .IsEqualTo((true, false, true));
        await Assert
            .That(seen.unified.Item1)
            .IsEquivalentTo(
                DiffLayout.Unified.RowsOf(Diffs.Modified, "src/Player.cs"),
                CollectionOrdering.Matching
            );
        await Assert
            .That((seen.unified.Item2, seen.unified.Item3, seen.unified.Item4))
            .IsEqualTo((false, true, false));
        await Assert
            .That(seen.splitAgain.Item1)
            .IsEquivalentTo(
                DiffLayout.Split.RowsOf(Diffs.Modified, "src/Player.cs"),
                CollectionOrdering.Matching
            );
        await Assert
            .That((seen.splitAgain.Item2, seen.splitAgain.Item3, seen.splitAgain.Item4))
            .IsEqualTo((true, false, true));
        await Assert.That(seen.Layout).IsSameReferenceAs(DiffLayout.Split);
    }

    [Test]
    public async Task Copying_side_by_side_rows_puts_old_then_new_lines_on_the_clipboard()
    {
        var copied = await HeadlessApp.Session.Dispatch(
            async () =>
            {
                var window = Show(
                    "Dark",
                    new DiffLinesView
                    {
                        Document = Diffs.Modified,
                        Subject = Diffs.SubjectOf(Diffs.Modified),
                    }
                );
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
                    "        position += velocity * delta;",
                    "        ClampToLevel();"
                )
            );
    }

    /// <summary>
    /// Only the default state, which the Changes screen hides the pane in, so there is no hint to
    /// read. The pane's other states are set by its view model's own behaviour,
    /// which has no public way in from a test of the view.
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    public async Task The_diff_pane_with_nothing_selected_says_nothing(string variant)
    {
        var texts = await RenderAsync(
            variant,
            () => new DiffPaneView { DataContext = DiffPanes.Pane() },
            $"diff-pane-nothing-selected-{variant.ToLowerInvariant()}.png",
            VisibleTextsOf,
            unframed: true
        );

        await Assert.That(texts).IsEmpty();
    }

    /// <summary>
    /// The pane hands the lines its row's path, so a folder whose diff is one picture shows the
    /// picture's name and no "Open in app" that would open the folder.
    /// </summary>
    [Test]
    [Arguments("art/hero.png", NodeKind.File, true)]
    [Arguments("art", NodeKind.Directory, false)]
    public async Task A_binary_card_offers_the_app_only_when_the_pane_shows_that_file(
        string relPath,
        NodeKind kind,
        bool offered
    )
    {
        var pane = DiffPanes.Pane(
            new FakeWorkingCopyDiff().Answers(
                "Index: art/hero.png\r\n"
                    + "===================================================================\r\n"
                    + "Cannot display: file marked as a binary type.\r\n"
                    + "svn:mime-type = image/png\r\n"
            )
        );
        await pane.RefetchAsync(
            ChangeRow.From(Entries.Entry(relPath, NodeStatus.Added, kind: kind)),
            "/studio/game/" + relPath
        );

        var texts = await RenderAsync(
            "Dark",
            () => new DiffPaneView { DataContext = pane },
            $"diff-pane-binary-{kind.ToString().ToLowerInvariant()}.png",
            VisibleTextsOf,
            unframed: true
        );

        await Assert.That(texts.Contains("Open in app")).IsEqualTo(offered);
        await Assert.That(texts.Contains("art/hero.png")).IsEqualTo(!offered);
    }

    [Test]
    public async Task The_context_dropdown_lists_every_choice_and_says_quietly_when_a_file_could_not_have_it()
    {
        var (items, selected, noteShown) = await RenderAsync(
            "Dark",
            () =>
                new DiffLinesView
                {
                    Document = Diffs.Modified,
                    Subject = "src/game.cs",
                    ContextOptions = DiffContextOption.All,
                    Context = DiffContextOption.All[1],
                    ContextUnavailable = true,
                },
            "context-unavailable.png",
            window =>
            {
                var choice = window.GetVisualDescendants().OfType<ComboBox>().Single();
                var note = window
                    .GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(text => text.Text == "Context unavailable for this file");
                return (choice.ItemCount, choice.SelectedItem, note.IsEffectivelyVisible);
            }
        );

        await Assert.That(items).IsEqualTo(4);
        await Assert.That(selected).IsSameReferenceAs(DiffContextOption.All[1]);
        await Assert.That(noteShown).IsTrue();
    }

    [Test]
    public async Task Without_choices_there_is_no_dropdown_and_without_a_fallback_no_note()
    {
        var (dropdownShown, noteShown) = await RenderAsync(
            "Dark",
            () => new DiffLinesView { Document = Diffs.Modified, Subject = "src/game.cs" },
            "context-none.png",
            window =>
            {
                var choice = window.GetVisualDescendants().OfType<ComboBox>().Single();
                var note = window
                    .GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(text => text.Text == "Context unavailable for this file");
                return (choice.IsEffectivelyVisible, note.IsEffectivelyVisible);
            }
        );

        await Assert.That(dropdownShown).IsFalse();
        await Assert.That(noteShown).IsFalse();
    }

    /// <summary>Two bindings deep — dropdown to lines view to pane — and the pick must reach the pane.</summary>
    [Test]
    public async Task Picking_in_the_dropdown_sets_the_changes_panes_context()
    {
        var diffs = new FakeWorkingCopyDiff().Answers(SvnDiffs.EditedText);
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var pane = DiffPanes.Pane(diffs, clock);
        var row = ChangeRow.From(Entries.Entry("src/game.cs"));
        var selecting = pane.SelectAsync(row, "/wc/src/game.cs");
        clock.Advance(DiffPaneViewModel.SelectionDebounce);
        await selecting;

        var picked = await RenderAsync(
            "Dark",
            () => new DiffPaneView { DataContext = pane },
            "context-pick.png",
            window =>
            {
                window.GetVisualDescendants().OfType<ComboBox>().Single().SelectedIndex = 3;
                Dispatcher.UIThread.RunJobs();
                return pane.Context;
            }
        );

        await Assert.That(picked).IsSameReferenceAs(DiffContextOption.All[3]);
        await Assert.That(diffs.Contexts.Last()).IsEqualTo(DiffContext.WholeFile);
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

        // The app's surfaces come from a preset, and the session's is dark; a light frame needs the
        // light preset's surfaces too, scoped to this window so parallel renders keep their own.
        if (variant == "Light")
        {
            window.Resources.MergedDictionaries.Add(
                new ResourceInclude((Uri?)null) { Source = ThemeCatalog.DefaultLight.Source }
            );
            window.RequestedThemeVariant = ThemeVariant.Light;
            window.Background = (IBrush?)window.FindResource("BgBrush");
        }

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

    private static object? ToneOf(Control control, string key) =>
        control.TryFindResource(key, control.ActualThemeVariant, out var brush) ? brush : null;

    private static int PixelAt(WriteableBitmap frame, double x, double y)
    {
        using var pixels = frame.Lock();
        return Marshal.ReadInt32(pixels.Address, (int)y * pixels.RowBytes + (int)x * 4);
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
