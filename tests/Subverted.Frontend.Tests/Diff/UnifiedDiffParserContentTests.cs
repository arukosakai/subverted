using Subverted.Frontend.Diff;
using static Subverted.Frontend.Tests.Diff.Line;

namespace Subverted.Frontend.Tests.Diff;

/// <summary>
/// What happened to a file's bytes, read off <c>svn diff</c> output captured from the
/// <c>subverted-diff</c> fixture — see <c>Diff/Captures/README.md</c>.
/// </summary>
public sealed class UnifiedDiffParserContentTests
{
    [Test]
    public async Task A_modified_file_has_one_hunk_per_changed_region_numbered_on_both_sides()
    {
        var file = CapturedDiff.FileIn("root.diff", "mod.txt");

        var text = await Assert.That(file.Content).IsTypeOf<TextChange>();
        await Assert.That(text!.Hunks).Count().IsEqualTo(2);
        await ExpectedHunk.Matches(
            text.Hunks[0],
            (1, 6, 1, 6),
            Context("line 1", 1, 1),
            Context("line 2", 2, 2),
            Removed("line 3", 3),
            Added("line three", 3),
            Context("line 4", 4, 4),
            Context("line 5", 5, 5),
            Context("line 6", 6, 6)
        );
        await ExpectedHunk.Matches(
            text.Hunks[1],
            (24, 7, 24, 7),
            Context("line 24", 24, 24),
            Context("line 25", 25, 25),
            Context("line 26", 26, 26),
            Removed("line 27", 27),
            Added("line twenty-seven", 27),
            Context("line 28", 28, 28),
            Context("line 29", 29, 29),
            Context("line 30", 30, 30)
        );
        await Assert.That(file.PropertyChanges).IsEmpty();
    }

    [Test]
    public async Task An_added_file_is_all_added_lines_numbered_from_one_on_the_new_side()
    {
        var file = CapturedDiff.FileIn("root.diff", "added.txt");

        await ExpectedHunk.IsOnlyHunkOf(
            file.Content,
            (0, 0, 1, 2),
            Added("fresh file", 1),
            Added("second line", 2)
        );
        await Assert.That(file.PropertyChanges).IsEmpty();
    }

    [Test]
    public async Task A_deleted_file_is_all_removed_lines_numbered_from_one_on_the_old_side()
    {
        var file = CapturedDiff.FileIn("root.diff", "del.txt");

        await ExpectedHunk.IsOnlyHunkOf(
            file.Content,
            (1, 2, 0, 0),
            Removed("doomed", 1),
            Removed("content", 2)
        );
    }

    [Test]
    public async Task A_deleted_file_with_properties_shows_only_its_content_because_svn_prints_no_property_section()
    {
        var file = CapturedDiff.OnlyFile("del-props.diff");

        await ExpectedHunk.IsOnlyHunkOf(
            file.Content,
            (1, 1, 0, 0),
            Removed("deleted with props", 1)
        );
        await Assert.That(file.PropertyChanges).IsEmpty();
    }

    [Test]
    public async Task A_replaced_file_diffs_the_replacement_against_what_it_replaced()
    {
        var file = CapturedDiff.OnlyFile("replaced.diff");

        await ExpectedHunk.IsOnlyHunkOf(
            file.Content,
            (1, 1, 1, 1),
            Removed("replace me", 1),
            Added("a replacement", 1)
        );
    }

    [Test]
    public async Task An_added_empty_file_is_a_text_change_with_no_hunks()
    {
        var file = CapturedDiff.OnlyFile("empty.diff");

        await Assert.That(file.Path).IsEqualTo("empty.txt");
        var text = await Assert.That(file.Content).IsTypeOf<TextChange>();
        await Assert.That(text!.Hunks).IsEmpty();
        await Assert.That(file.PropertyChanges).IsEmpty();
    }

    [Test]
    [Arguments("missing.diff")]
    [Arguments("moved.diff")]
    [Arguments("empty-del.diff")]
    public async Task What_svn_prints_nothing_for_parses_to_the_empty_document(string captureName)
    {
        await Assert.That(CapturedDiff.Read(captureName)).IsEqualTo(string.Empty);
        await Assert.That(CapturedDiff.Parse(captureName)).IsSameReferenceAs(DiffDocument.Empty);
    }

    [Test]
    public async Task A_file_moved_and_then_edited_diffs_against_its_source()
    {
        var file = CapturedDiff.OnlyFile("movededit.diff");

        await Assert.That(file.Path).IsEqualTo("movededit.txt");
        await ExpectedHunk.IsOnlyHunkOf(
            file.Content,
            (1, 1, 1, 2),
            Context("moving and editing", 1, 1),
            Added("edited after the move", 2)
        );
    }

    [Test]
    public async Task The_source_of_a_move_reads_as_a_whole_file_deletion()
    {
        var file = CapturedDiff.OnlyFile("moveme.diff");

        await Assert.That(file.Path).IsEqualTo("moveme.txt");
        await ExpectedHunk.IsOnlyHunkOf(file.Content, (1, 1, 0, 0), Removed("moving", 1));
    }

    [Test]
    public async Task A_missing_newline_on_the_old_side_marks_only_the_removed_line()
    {
        var file = CapturedDiff.FileIn("root.diff", "noeol-old.txt");

        await ExpectedHunk.IsOnlyHunkOf(
            file.Content,
            (1, 1, 1, 2),
            Removed("no newline at end", 1, endsWithoutNewline: true),
            Added("no newline at end", 1),
            Added("now with more", 2)
        );
    }

    [Test]
    public async Task A_missing_newline_on_the_new_side_marks_only_the_last_added_line()
    {
        var file = CapturedDiff.FileIn("root.diff", "noeol-new.txt");

        await ExpectedHunk.IsOnlyHunkOf(
            file.Content,
            (1, 1, 1, 2),
            Context("ends with newline", 1, 1),
            Added("but not any more", 2, endsWithoutNewline: true)
        );
    }

    [Test]
    public async Task A_missing_newline_on_both_sides_marks_the_last_line_of_each()
    {
        var file = CapturedDiff.FileIn("root.diff", "noeol-both.txt");

        await ExpectedHunk.IsOnlyHunkOf(
            file.Content,
            (1, 2, 1, 2),
            Context("first", 1, 1),
            Removed("last without", 2, endsWithoutNewline: true),
            Added("last changed", 2, endsWithoutNewline: true)
        );
    }

    [Test]
    [Arguments("crlf-raw.txt")]
    [Arguments("crlf-styled.txt")]
    public async Task Content_with_crlf_endings_loses_its_carriage_returns_whether_or_not_eol_style_is_set(
        string path
    )
    {
        await Assert.That(CapturedDiff.Read("root.diff")).Contains("\n-two\r\n+TWO\r\n");

        var file = CapturedDiff.FileIn("root.diff", path);

        await ExpectedHunk.IsOnlyHunkOf(
            file.Content,
            (1, 3, 1, 3),
            Context("one", 1, 1),
            Removed("two", 2),
            Added("TWO", 2),
            Context("three", 3, 3)
        );
    }

    [Test]
    public async Task A_lone_carriage_return_ends_a_line_because_svn_counts_it_as_one()
    {
        await Assert.That(CapturedDiff.Read("mac-cr.diff")).Contains(" one\r-two\r+TWO\r three\n");

        var file = CapturedDiff.OnlyFile("mac-cr.diff");

        await ExpectedHunk.IsOnlyHunkOf(
            file.Content,
            (1, 3, 1, 3),
            Context("one", 1, 1),
            Removed("two", 2),
            Added("TWO", 2),
            Context("three", 3, 3)
        );
    }

    [Test]
    public async Task Content_lines_shaped_like_headers_stay_content_because_hunk_counts_decide_where_a_hunk_ends()
    {
        var file = CapturedDiff.FileIn("root.diff", "headers.txt");

        var text = await Assert.That(file.Content).IsTypeOf<TextChange>();
        await Assert.That(text!.Hunks).Count().IsEqualTo(2);
        await ExpectedHunk.Matches(
            text.Hunks[0],
            (1, 7, 1, 7),
            Context("plain", 1, 1),
            Context("Index: foo", 2, 2),
            Context(new string('=', 67), 3, 3),
            Removed("--- x\t(revision 1)", 4),
            Added("--- x\t(revision 2)", 4),
            Context("+++ y\t(working copy)", 5, 5),
            Context("@@ -1 +1 @@", 6, 6),
            Context("Property changes on: z", 7, 7)
        );
        await ExpectedHunk.Matches(
            text.Hunks[1],
            (10, 4, 10, 5),
            Context("## -0,0 +1 ##", 10, 10),
            Context("\\ No newline at end of file", 11, 11),
            Context("Cannot display: file marked as a binary type.", 12, 12),
            Added("Index: added inside", 13),
            Context("tail", 13, 14)
        );
        await Assert.That(file.PropertyChanges).IsEmpty();
    }

    [Test]
    public async Task A_modified_binary_file_is_a_binary_change_naming_its_mime_type()
    {
        var file = CapturedDiff.FileIn("root.diff", "bin-mod.bin");

        await Assert.That(file.Content).IsEqualTo(new BinaryChange("application/octet-stream"));
        await Assert.That(file.PropertyChanges).IsEmpty();
    }

    [Test]
    public async Task A_deleted_binary_file_is_a_binary_change_with_nothing_else()
    {
        var file = CapturedDiff.OnlyFile("bin-del.diff");

        await Assert.That(file.Path).IsEqualTo("bin-del.bin");
        await Assert.That(file.Content).IsEqualTo(new BinaryChange("application/octet-stream"));
        await Assert.That(file.PropertyChanges).IsEmpty();
    }
}
