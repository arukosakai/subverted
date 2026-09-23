using Subverted.Frontend.Diff;
using static Subverted.Frontend.Tests.Diff.Line;

namespace Subverted.Frontend.Tests.Diff;

/// <summary>
/// Hand-written inputs, deliberately: none of these shapes appeared in any capture, and they exist
/// only to pin what <see cref="UnifiedDiffParser.Parse"/> promises for text it does not recognise —
/// skip it, keep what was read, never throw.
/// </summary>
public sealed class UnifiedDiffParserUnrecognisedTextTests
{
    [Test]
    public async Task Hand_written_empty_text_is_the_empty_document()
    {
        await Assert
            .That(UnifiedDiffParser.Parse(string.Empty))
            .IsSameReferenceAs(DiffDocument.Empty);
    }

    [Test]
    public async Task Hand_written_text_with_no_index_header_is_the_empty_document()
    {
        var document = UnifiedDiffParser.Parse("svn: warning: something\n@@ -1 +1 @@\n-a\n+b\n");

        await Assert.That(document).IsSameReferenceAs(DiffDocument.Empty);
    }

    [Test]
    public async Task Hand_written_text_before_the_first_index_header_is_skipped()
    {
        var document = UnifiedDiffParser.Parse("noise\nIndex: a.txt\n@@ -1 +1 @@\n-x\n+y\n");

        var file = document.Files.Single();
        await Assert.That(file.Path).IsEqualTo("a.txt");
        await ExpectedHunk.IsOnlyHunkOf(file.Content, (1, 1, 1, 1), Removed("x", 1), Added("y", 1));
    }

    [Test]
    public async Task Hand_written_hunk_with_an_unreadable_header_is_skipped_body_and_all()
    {
        var document = UnifiedDiffParser.Parse(
            "Index: a.txt\n@@ -x +1 @@\n-x\n+y\n@@ -5 +5 @@\n-p\n+q\n"
        );

        await ExpectedHunk.IsOnlyHunkOf(
            document.Files.Single().Content,
            (5, 1, 5, 1),
            Removed("p", 5),
            Added("q", 5)
        );
    }

    [Test]
    public async Task Hand_written_hunk_cut_off_by_the_end_of_the_text_keeps_the_lines_it_had()
    {
        var document = UnifiedDiffParser.Parse("Index: a.txt\n@@ -1,3 +1,3 @@\n same\n");

        await ExpectedHunk.IsOnlyHunkOf(
            document.Files.Single().Content,
            (1, 3, 1, 3),
            Context("same", 1, 1)
        );
    }

    [Test]
    public async Task Hand_written_hunk_cut_off_by_the_next_index_header_leaves_that_file_to_be_read()
    {
        var document = UnifiedDiffParser.Parse(
            "Index: a.txt\n@@ -1,3 +1,3 @@\n same\nIndex: b.txt\n@@ -1 +1 @@\n-x\n+y\n"
        );

        await Assert.That(document.Files).Count().IsEqualTo(2);
        await ExpectedHunk.IsOnlyHunkOf(
            document.Files[0].Content,
            (1, 3, 1, 3),
            Context("same", 1, 1)
        );
        await Assert.That(document.Files[1].Path).IsEqualTo("b.txt");
        await ExpectedHunk.IsOnlyHunkOf(
            document.Files[1].Content,
            (1, 1, 1, 1),
            Removed("x", 1),
            Added("y", 1)
        );
    }

    [Test]
    public async Task Hand_written_empty_line_inside_a_hunk_ends_it_because_svn_prefixes_even_blank_lines()
    {
        var document = UnifiedDiffParser.Parse("Index: a.txt\n@@ -1,2 +1,2 @@\n one\n\n two\n");

        await ExpectedHunk.IsOnlyHunkOf(
            document.Files.Single().Content,
            (1, 2, 1, 2),
            Context("one", 1, 1)
        );
    }

    [Test]
    public async Task Hand_written_binary_notice_without_a_mime_type_line_names_no_mime_type()
    {
        var document = UnifiedDiffParser.Parse(
            "Index: a.bin\nCannot display: file marked as a binary type.\nIndex: b.txt\n"
        );

        await Assert.That(document.Files[0].Content).IsEqualTo(new BinaryChange(MimeType: null));
        await Assert.That(document.Files[1].Path).IsEqualTo("b.txt");
    }

    [Test]
    public async Task Hand_written_binary_notice_at_the_very_end_names_no_mime_type()
    {
        var document = UnifiedDiffParser.Parse(
            "Index: a.bin\nCannot display: file marked as a binary type."
        );

        await Assert
            .That(document.Files.Single().Content)
            .IsEqualTo(new BinaryChange(MimeType: null));
    }
}
