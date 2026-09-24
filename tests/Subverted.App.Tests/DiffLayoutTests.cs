using Subverted.App.Presentation;
using Subverted.Frontend.Diff;
using TUnit.Assertions.Enums;
using static Subverted.App.Tests.Diffs;

namespace Subverted.App.Tests;

/// <summary>
/// The split layout end to end over a document. How the unified layout flattens one is
/// <see cref="DiffRowsTests"/>; the pairing rules themselves are <see cref="SplitLinesTests"/>.
/// </summary>
public sealed class DiffLayoutTests
{
    [Test]
    public async Task Each_hunk_opens_with_its_range_spanning_both_sides_and_its_lines_paired()
    {
        DiffRow[] expected =
        [
            new DiffHunkRow("@@ -10,7 +10,8 @@"),
            Both(Context(10, 10, "    public void Update(float delta)")),
            Both(Context(11, 11, "    {")),
            Both(Context(12, 12, "        velocity += gravity * delta;")),
            new DiffSplitRow(
                Removed(13, "        position += velocity;"),
                Added(13, "        position += velocity * delta;")
            ),
            new DiffSplitRow(null, Added(14, "        ClampToLevel();")),
            Both(Context(14, 15, "    }")),
            Both(Context(15, 16, "")),
            Both(Context(16, 17, "    private void Jump()")),
            new DiffHunkRow("@@ -40 +41 @@"),
            new DiffSplitRow(
                Removed(40, "    const int MaxJumps = 1;"),
                Added(41, "    const int MaxJumps = 2;")
            ),
        ];

        await Assert
            .That(DiffLayout.Split.RowsOf(Modified, "src/Player.cs"))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Headers_cards_and_property_sections_are_laid_out_as_in_the_unified_layout()
    {
        DiffRow[] expected =
        [
            new DiffFileHeaderRow("levels/forest.map"),
            new DiffHunkRow("@@ -3 +3 @@"),
            new DiffSplitRow(Removed(3, "trees 40"), Added(3, "trees 55")),
            new DiffFileHeaderRow("levels/music.ogg"),
            new DiffBinaryRow("levels/music.ogg", null, IsOnlyFile: false),
            new DiffFileHeaderRow("levels/cave.map"),
            new DiffHunkRow("@@ -0,0 +1,2 @@"),
            new DiffSplitRow(null, Added(1, "size 32 32")),
            new DiffSplitRow(null, Added(2, "spawn 1 1")),
            new DiffFileHeaderRow("levels"),
            new DiffPropertySectionRow("levels"),
            new DiffPropertyRow("svn:ignore", PropertyChangeKind.Deleted),
            new DiffSplitRow(Removed(1, "bin"), null),
            new DiffSplitRow(Removed(2, "obj"), null),
        ];

        await Assert
            .That(DiffLayout.Split.RowsOf(WholeDirectory, "levels"))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_text_change_with_no_hunks_says_there_are_no_lines_in_either_layout()
    {
        await Assert
            .That(DiffLayout.Split.RowsOf(Document(Text("empty.txt")), "empty.txt"))
            .IsEquivalentTo(
                new DiffRow[] { new DiffNoLinesRow("empty.txt") },
                CollectionOrdering.Matching
            );
    }

    /// <summary>Property values drop the no-newline marker in the split layout too, but file content keeps it.</summary>
    [Test]
    public async Task A_missing_final_newline_is_marked_on_file_content_but_not_on_a_property_value()
    {
        var value = new PropertyChange(
            "svn:eol-style",
            PropertyChangeKind.Modified,
            [
                new Hunk(
                    1,
                    1,
                    1,
                    1,
                    [
                        Removed(1, "LF", endsWithoutNewline: true),
                        Added(1, "native", endsWithoutNewline: true),
                    ]
                ),
            ]
        );
        var document = Document(
            new FileDiff(
                "src/Player.cs",
                new TextChange([new Hunk(1, 0, 1, 1, [Added(1, "}", endsWithoutNewline: true)])]),
                [value]
            )
        );

        var rows = DiffLayout
            .Split.RowsOf(document, "src/Player.cs")
            .OfType<DiffSplitRow>()
            .ToList();

        await Assert
            .That(rows)
            .IsEquivalentTo(
                [
                    new DiffSplitRow(null, Added(1, "}", endsWithoutNewline: true)),
                    new DiffSplitRow(Removed(1, "LF"), Added(1, "native")),
                ],
                CollectionOrdering.Matching
            );
    }

    /// <summary>Real <c>svn diff</c> output through the parser, so the pairing sees what SVN actually prints.</summary>
    [Test]
    public async Task A_captured_svn_diff_of_content_and_a_property_lays_out_side_by_side()
    {
        var document = UnifiedDiffParser.Parse(SvnDiffs.EditedTextAndProperty);

        DiffRow[] expected =
        [
            new DiffHunkRow("@@ -1 +1 @@"),
            new DiffSplitRow(Removed(1, "content of both"), Added(1, "changed")),
            new DiffPropertySectionRow("both.txt"),
            new DiffPropertyRow("svn:mime-type", PropertyChangeKind.Added),
            new DiffSplitRow(null, Added(1, "text/plain")),
        ];

        await Assert
            .That(DiffLayout.Split.RowsOf(document, "both.txt"))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_captured_svn_diff_of_an_added_line_puts_it_beside_filler()
    {
        var document = UnifiedDiffParser.Parse(SvnDiffs.EditedText);

        DiffRow[] expected =
        [
            new DiffHunkRow("@@ -1 +1,2 @@"),
            Both(Context(1, 1, "child")),
            new DiffSplitRow(null, Added(2, "edited too")),
        ];

        await Assert
            .That(DiffLayout.Split.RowsOf(document, SubjectOf(document)))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task The_unified_layout_lists_every_line_as_a_row_of_its_own_paired_with_its_counterpart()
    {
        await Assert
            .That(DiffLayout.Unified.RowsOf(Document(Text("a.txt", OneLineHunk)), "a.txt"))
            .IsEquivalentTo(
                new DiffRow[]
                {
                    new DiffHunkRow("@@ -40 +41 @@"),
                    new DiffTextRow(
                        Removed(40, "    const int MaxJumps = 1;"),
                        Added(41, "    const int MaxJumps = 2;")
                    ),
                    new DiffTextRow(
                        Added(41, "    const int MaxJumps = 2;"),
                        Removed(40, "    const int MaxJumps = 1;")
                    ),
                },
                CollectionOrdering.Matching
            );
    }

    private static DiffSplitRow Both(DiffLine context) => new(context, context);
}
