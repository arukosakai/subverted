using Subverted.App.Presentation;
using static Subverted.App.Tests.Diffs;

namespace Subverted.App.Tests;

public sealed class DiffClipboardTextTests
{
    private static readonly DiffRow[] Rows =
    [
        new DiffHunkRow("@@ -1,2 +1,2 @@"),
        new DiffTextRow(Context(1, 1, "first")),
        new DiffTextRow(Removed(2, "second")),
        new DiffTextRow(Added(2, "second, changed")),
        new DiffPropertySectionRow("a.txt"),
        new DiffPropertyRow("svn:eol-style", Frontend.Diff.PropertyChangeKind.Added),
        new DiffTextRow(Added(1, "native")),
    ];

    /// <summary>Selection order is click order; the clipboard gets the lines as the file has them.</summary>
    [Test]
    public async Task Selected_lines_are_copied_in_list_order_without_their_signs()
    {
        await Assert
            .That(DiffClipboardText.Of(Rows, [3, 1, 2]))
            .IsEqualTo(string.Join(Environment.NewLine, "first", "second", "second, changed"));
    }

    [Test]
    public async Task Headers_in_the_selection_are_left_out()
    {
        await Assert
            .That(DiffClipboardText.Of(Rows, [0, 1, 4, 5, 6]))
            .IsEqualTo(string.Join(Environment.NewLine, "first", "native"));
    }

    private static readonly DiffRow[] SplitRows =
    [
        new DiffHunkRow("@@ -1,4 +1,4 @@"),
        new DiffSplitRow(Context(1, 1, "first"), Context(1, 1, "first")),
        new DiffSplitRow(Removed(2, "second"), Added(2, "second, changed")),
        new DiffSplitRow(Removed(3, "third"), null),
        new DiffSplitRow(Context(4, 3, "fourth"), Context(4, 3, "fourth")),
        new DiffHunkRow("@@ -9 +8,2 @@"),
        new DiffSplitRow(Removed(9, "ninth"), Added(8, "eighth")),
        new DiffSplitRow(null, Added(9, "ninth, again")),
    ];

    /// <summary>The same lines copy the same text in either layout: a context line once, a run old before new.</summary>
    [Test]
    public async Task Side_by_side_lines_copy_in_the_order_a_unified_diff_lists_them()
    {
        await Assert
            .That(DiffClipboardText.Of(SplitRows, [4, 3, 2, 1]))
            .IsEqualTo(
                string.Join(
                    Environment.NewLine,
                    "first",
                    "second",
                    "third",
                    "second, changed",
                    "fourth"
                )
            );
    }

    /// <summary>
    /// Two runs picked with the context between them left out: each run is still whole, as the
    /// same pick in the unified layout copies it.
    /// </summary>
    [Test]
    public async Task Runs_picked_with_a_gap_between_them_keep_each_runs_lines_together()
    {
        DiffRow[] rows =
        [
            new DiffSplitRow(Context(1, 1, "a"), Context(1, 1, "a")),
            new DiffSplitRow(Removed(2, "b"), Added(2, "B")),
            new DiffSplitRow(Context(3, 3, "c"), Context(3, 3, "c")),
            new DiffSplitRow(Removed(4, "d"), Added(4, "D")),
        ];

        await Assert
            .That(DiffClipboardText.Of(rows, [3, 1]))
            .IsEqualTo(string.Join(Environment.NewLine, "b", "B", "d", "D"));
    }

    /// <summary>Rows next to each other in one run are one run, however they were picked.</summary>
    [Test]
    public async Task Adjacent_rows_of_one_run_stay_one_run()
    {
        await Assert
            .That(DiffClipboardText.Of(SplitRows, [2, 3]))
            .IsEqualTo(string.Join(Environment.NewLine, "second", "third", "second, changed"));
    }

    /// <summary>A hunk header closes a run as context does, so one hunk's new lines never trail the next's.</summary>
    [Test]
    public async Task A_header_between_two_runs_keeps_each_runs_lines_together()
    {
        await Assert
            .That(DiffClipboardText.Of(SplitRows, [2, 5, 6, 7]))
            .IsEqualTo(
                string.Join(
                    Environment.NewLine,
                    "second",
                    "second, changed",
                    "ninth",
                    "eighth",
                    "ninth, again"
                )
            );
    }

    [Test]
    public async Task A_run_selected_alone_copies_its_old_side_then_its_new_side()
    {
        await Assert
            .That(DiffClipboardText.Of(SplitRows, [6, 7]))
            .IsEqualTo(string.Join(Environment.NewLine, "ninth", "eighth", "ninth, again"));
    }

    [Test]
    public async Task Nothing_selected_copies_nothing()
    {
        await Assert.That(DiffClipboardText.Of(Rows, [])).IsEqualTo(string.Empty);
    }
}
