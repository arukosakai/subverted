using Subverted.App.Presentation;
using Subverted.Frontend.Diff;
using TUnit.Assertions.Enums;
using static Subverted.App.Tests.Diffs;

namespace Subverted.App.Tests;

public sealed class DiffRowsTests
{
    [Test]
    public async Task A_missing_final_newline_is_marked_on_file_content_but_not_on_a_property_value()
    {
        var value = new PropertyChange(
            "svn:eol-style",
            PropertyChangeKind.Added,
            [new Hunk(0, 0, 1, 1, [Added(1, "native", endsWithoutNewline: true)])]
        );
        var document = Document(
            new FileDiff(
                "src/Player.cs",
                new TextChange([new Hunk(1, 0, 1, 1, [Added(1, "}", endsWithoutNewline: true)])]),
                [value]
            )
        );

        var marked = DiffLayout
            .Unified.RowsOf(document, "src/Player.cs")
            .OfType<DiffTextRow>()
            .Select(row => (row.Line.Text, row.Line.EndsWithoutNewline));

        await Assert
            .That(marked)
            .IsEquivalentTo([("}", true), ("native", false)], CollectionOrdering.Matching);
    }

    [Test]
    public async Task An_empty_document_has_no_rows()
    {
        await Assert.That(DiffLayout.Unified.RowsOf(DiffDocument.Empty, "")).IsEmpty();
    }

    [Test]
    public async Task A_single_file_has_no_header_and_each_hunk_opens_with_its_range()
    {
        DiffRow[] expected =
        [
            new DiffHunkRow("@@ -10,7 +10,8 @@"),
            .. UnifiedLines.Of(ModifiedHunk.Lines),
            new DiffHunkRow("@@ -40 +41 @@"),
            .. UnifiedLines.Of(OneLineHunk.Lines),
        ];

        await Assert
            .That(DiffLayout.Unified.RowsOf(Modified, "src/Player.cs"))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Several_files_each_open_with_a_header_naming_them()
    {
        var rows = DiffLayout.Unified.RowsOf(WholeDirectory, "levels");

        DiffRow[] expected =
        [
            new DiffFileHeaderRow("levels/forest.map"),
            new DiffHunkRow("@@ -3 +3 @@"),
            new DiffTextRow(Removed(3, "trees 40"), Added(3, "trees 55")),
            new DiffTextRow(Added(3, "trees 55"), Removed(3, "trees 40")),
            new DiffFileHeaderRow("levels/music.ogg"),
            new DiffBinaryRow("levels/music.ogg", null, IsOnlyFile: false),
            new DiffFileHeaderRow("levels/cave.map"),
            new DiffHunkRow("@@ -0,0 +1,2 @@"),
            new DiffTextRow(Added(1, "size 32 32")),
            new DiffTextRow(Added(2, "spawn 1 1")),
            new DiffFileHeaderRow("levels"),
            new DiffPropertySectionRow("levels"),
            new DiffPropertyRow("svn:ignore", PropertyChangeKind.Deleted),
            new DiffTextRow(Removed(1, "bin")),
            new DiffTextRow(Removed(2, "obj")),
        ];

        await Assert.That(rows).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    /// <summary>A count of one is left out, as diff prints it; zero and many are kept.</summary>
    [Test]
    [Arguments(12, 6, 12, 7, "@@ -12,6 +12,7 @@")]
    [Arguments(3, 1, 3, 1, "@@ -3 +3 @@")]
    [Arguments(3, 1, 3, 2, "@@ -3 +3,2 @@")]
    [Arguments(3, 2, 3, 1, "@@ -3,2 +3 @@")]
    [Arguments(0, 0, 1, 1, "@@ -0,0 +1 @@")]
    [Arguments(1, 2, 0, 0, "@@ -1,2 +0,0 @@")]
    public async Task A_hunk_range_reads_as_svn_prints_it(
        int oldStart,
        int oldCount,
        int newStart,
        int newCount,
        string expected
    )
    {
        var hunk = new Hunk(oldStart, oldCount, newStart, newCount, []);

        await Assert
            .That(DiffLayout.Unified.RowsOf(Document(Text("a.txt", hunk)), "a.txt"))
            .IsEquivalentTo(
                new DiffRow[] { new DiffHunkRow(expected) },
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task A_text_change_with_no_hunks_says_there_are_no_lines()
    {
        await Assert
            .That(DiffLayout.Unified.RowsOf(Document(Text("empty.txt")), "empty.txt"))
            .IsEquivalentTo(
                new DiffRow[] { new DiffNoLinesRow("empty.txt") },
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task A_lone_binary_file_is_a_card_that_describes_the_selected_row()
    {
        DiffRow[] expected =
        [
            new DiffBinaryRow("art/hero.png", "image/png", IsOnlyFile: true),
            new DiffPropertySectionRow("art/hero.png"),
            new DiffPropertyRow("svn:mime-type", PropertyChangeKind.Added),
            new DiffTextRow(Added(1, "image/png")),
        ];

        await Assert
            .That(DiffLayout.Unified.RowsOf(Binary, "art/hero.png"))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    /// <summary>With no content change there is nothing before the property section — not even a header.</summary>
    [Test]
    public async Task A_property_only_change_is_its_property_section_alone()
    {
        DiffRow[] expected =
        [
            new DiffPropertySectionRow("src/Player.cs"),
            new DiffPropertyRow("svn:eol-style", PropertyChangeKind.Added),
            new DiffTextRow(Added(1, "native")),
            new DiffPropertyRow("svn:keywords", PropertyChangeKind.Modified),
            new DiffTextRow(Removed(1, "Id"), Added(1, "Id Rev")),
            new DiffTextRow(Added(1, "Id Rev"), Removed(1, "Id")),
        ];

        await Assert
            .That(DiffLayout.Unified.RowsOf(PropertiesOnly, "src/Player.cs"))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Content_comes_before_the_property_section()
    {
        DiffRow[] expected =
        [
            new DiffHunkRow("@@ -40 +41 @@"),
            .. UnifiedLines.Of(OneLineHunk.Lines),
            new DiffPropertySectionRow("src/Player.cs"),
            new DiffPropertyRow("svn:keywords", PropertyChangeKind.Modified),
            new DiffTextRow(Removed(1, "Id"), Added(1, "Id Rev")),
            new DiffTextRow(Added(1, "Id Rev"), Removed(1, "Id")),
        ];

        await Assert
            .That(DiffLayout.Unified.RowsOf(ContentAndProperties, "src/Player.cs"))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    /// <summary>The name row opens a property's first hunk; only the later ones need a separator.</summary>
    [Test]
    public async Task Only_a_propertys_later_hunks_get_a_separator()
    {
        var externals = new PropertyChange(
            "svn:externals",
            PropertyChangeKind.Modified,
            [
                new Hunk(1, 1, 1, 1, [Removed(1, "^/lib/a a"), Added(1, "^/lib/a@12 a")]),
                new Hunk(9, 0, 10, 1, [Added(10, "^/lib/z z")]),
            ]
        );

        DiffRow[] expected =
        [
            new DiffPropertySectionRow("."),
            new DiffPropertyRow("svn:externals", PropertyChangeKind.Modified),
            new DiffTextRow(Removed(1, "^/lib/a a"), Added(1, "^/lib/a@12 a")),
            new DiffTextRow(Added(1, "^/lib/a@12 a"), Removed(1, "^/lib/a a")),
            new DiffHunkRow("## -9,0 +10 ##"),
            new DiffTextRow(Added(10, "^/lib/z z")),
        ];

        await Assert
            .That(DiffLayout.Unified.RowsOf(Document(new FileDiff(".", null, [externals])), "."))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    /// <summary>
    /// An added folder holding one picture: SVN prints a section for the picture only. The card
    /// must not offer the folder's size or open the folder as if it were the picture.
    /// </summary>
    [Test]
    public async Task A_folder_holding_one_binary_file_names_it_and_describes_only_the_folder()
    {
        DiffRow[] expected =
        [
            new DiffFileHeaderRow("art/hero.png"),
            new DiffBinaryRow("art/hero.png", "image/png", IsOnlyFile: false),
            new DiffPropertySectionRow("art/hero.png"),
            new DiffPropertyRow("svn:mime-type", PropertyChangeKind.Added),
            new DiffTextRow(Added(1, "image/png")),
        ];

        await Assert
            .That(DiffLayout.Unified.RowsOf(Binary, "art"))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_folder_holding_one_text_file_names_it_above_its_lines()
    {
        var rows = DiffLayout.Unified.RowsOf(Modified, "src");

        await Assert.That(rows[0]).IsEqualTo(new DiffFileHeaderRow("src/Player.cs"));
    }

    /// <summary>SVN names the diffed folder's own section <c>.</c>, which reads as nothing at all.</summary>
    [Test]
    public async Task The_diffed_folders_own_section_is_headed_as_this_folder()
    {
        var header = new DiffFileHeaderRow("");

        await Assert.That(header.Title).IsEqualTo("This folder");
    }

    [Test]
    public async Task A_files_header_is_its_path()
    {
        await Assert.That(new DiffFileHeaderRow("src/Player.cs").Title).IsEqualTo("src/Player.cs");
    }
}
