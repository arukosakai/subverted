using Subverted.Frontend.Diff;
using static Subverted.Frontend.Tests.Diff.Line;

namespace Subverted.Frontend.Tests.Diff;

/// <summary>
/// A committed revision's diff, as <c>svn diff -c N URL@N</c> prints it for the History view —
/// captured from the <c>subverted-history</c> fixture, see <c>Diff/Captures/README.md</c>. The
/// headers read <c>(revision N)</c> on both sides, and paths are relative to the URL asked about.
/// </summary>
public sealed class UnifiedDiffParserRevisionTests
{
    [Test]
    public async Task An_edit_in_a_revision_reads_as_the_same_hunks_a_local_edit_does()
    {
        var file = CapturedDiff.OnlyFile("rev-mod.diff");

        await Assert.That(file.Path).IsEqualTo("a.txt");
        await ExpectedHunk.IsOnlyHunkOf(
            file.Content,
            (1, 3, 1, 3),
            Context("one", 1, 1),
            Removed("two", 2),
            Added("TWO", 2),
            Context("three", 3, 3)
        );
        await Assert.That(file.PropertyChanges).IsEmpty();
    }

    [Test]
    public async Task A_file_the_revision_deleted_is_all_removed_lines()
    {
        var file = CapturedDiff.OnlyFile("rev-del.diff");

        await Assert.That(file.Path).IsEqualTo("readme.txt");
        await ExpectedHunk.IsOnlyHunkOf(
            file.Content,
            (1, 2, 0, 0),
            Removed("readme", 1),
            Removed("more", 2)
        );
    }

    [Test]
    public async Task The_old_name_of_a_file_the_revision_moved_is_all_removed_lines()
    {
        var file = CapturedDiff.OnlyFile("rev-moved-away.diff");

        await ExpectedHunk.IsOnlyHunkOf(
            file.Content,
            (1, 3, 0, 0),
            Removed("one", 1),
            Removed("TWO", 2),
            Removed("three", 3)
        );
    }

    /// <summary>SVN compares the new name with where it was copied from, and they are the same.</summary>
    [Test]
    public async Task The_new_name_of_an_unedited_move_has_no_sections_at_all()
    {
        await Assert.That(CapturedDiff.Parse("rev-copy-unedited.diff").Files).IsEmpty();
    }

    [Test]
    public async Task A_revision_that_changed_content_and_a_property_carries_both()
    {
        var file = CapturedDiff.OnlyFile("rev-props.diff");

        await ExpectedHunk.IsOnlyHunkOf(
            file.Content,
            (1, 1, 1, 2),
            Context("readme", 1, 1),
            Added("more", 2)
        );
        var property = file.PropertyChanges.Single();
        await Assert.That(property.Name).IsEqualTo("svn:eol-style");
        await Assert.That(property.Kind).IsEqualTo(PropertyChangeKind.Added);
        await ExpectedHunk.Matches(
            property.Hunks.Single(),
            (0, 0, 1, 1),
            Added("native", 1, endsWithoutNewline: true)
        );
    }

    /// <summary>
    /// Added with a MIME type, a binary comes as two sections under one header — the notice, then
    /// the property — exactly as a local add does, and is still one file.
    /// </summary>
    [Test]
    public async Task A_binary_the_revision_added_is_one_file_with_its_mime_type_property()
    {
        var file = CapturedDiff.OnlyFile("rev-bin-add.diff");

        await Assert.That(file.Path).IsEqualTo("hero.png");
        await Assert.That(file.Content).IsEqualTo(new BinaryChange("application/octet-stream"));
        await Assert.That(file.PropertyChanges.Single().Name).IsEqualTo("svn:mime-type");
    }

    [Test]
    public async Task A_binary_the_revision_changed_is_the_notice_alone()
    {
        var file = CapturedDiff.OnlyFile("rev-bin-mod.diff");

        await Assert.That(file.Content).IsEqualTo(new BinaryChange("application/octet-stream"));
        await Assert.That(file.PropertyChanges).IsEmpty();
    }

    [Test]
    public async Task A_last_line_committed_without_a_newline_is_marked()
    {
        var file = CapturedDiff.OnlyFile("rev-no-newline.diff");

        await ExpectedHunk.IsOnlyHunkOf(
            file.Content,
            (1, 3, 1, 4),
            Context("one", 1, 1),
            Context("TWO", 2, 2),
            Context("three", 3, 3),
            Added("four", 4, endsWithoutNewline: true)
        );
    }

    /// <summary>Asked about a directory, SVN names its files relative to that directory.</summary>
    [Test]
    public async Task A_directorys_revision_diff_names_its_files_relative_to_the_directory()
    {
        var document = CapturedDiff.Parse("rev-dir.diff");

        await Assert.That(document.Files.Select(file => file.Path)).IsEquivalentTo(["hero.png"]);
    }

    [Test]
    public async Task The_repository_roots_revision_diff_lists_both_sides_of_a_move()
    {
        var document = CapturedDiff.Parse("rev-root.diff");

        await Assert
            .That(document.Files.Select(file => file.Path))
            .IsEquivalentTo(["a.txt", "b.txt"]);
        await Assert.That(document.Files[1].Content).IsTypeOf<TextChange>();
    }

    [Test]
    [Arguments("rev-icon.diff", "icon@2x.png.txt")]
    [Arguments("rev-odd-name.diff", "100% done file.txt")]
    public async Task A_name_that_was_escaped_in_the_url_comes_back_as_itself(
        string capture,
        string name
    )
    {
        await Assert.That(CapturedDiff.OnlyFile(capture).Path).IsEqualTo(name);
    }
}
