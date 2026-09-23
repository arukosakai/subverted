using Subverted.Frontend.Diff;
using TUnit.Assertions.Enums;
using static Subverted.Frontend.Tests.Diff.Line;

namespace Subverted.Frontend.Tests.Diff;

/// <summary>
/// SVN's <c>Property changes on:</c> sections, read off captured <c>svn diff</c> output — see
/// <c>Diff/Captures/README.md</c>.
/// </summary>
public sealed class UnifiedDiffParserPropertyTests
{
    [Test]
    public async Task A_file_whose_properties_alone_changed_has_no_content_change()
    {
        var file = CapturedDiff.FileIn("root.diff", "proponly.txt");

        await Assert.That(file.Content).IsNull();
        await Assert.That(file.PropertyChanges).Count().IsEqualTo(1);
        await ExpectProperty(
            file.PropertyChanges[0],
            "only",
            PropertyChangeKind.Added,
            (0, 0, 1, 1),
            Added("yes", 1, endsWithoutNewline: true)
        );
    }

    [Test]
    public async Task Properties_added_modified_and_deleted_on_one_file_are_each_read_with_their_kind_in_svns_order()
    {
        var file = CapturedDiff.FileIn("root.diff", "propfile.txt");

        await Assert.That(file.Content).IsNull();
        await Assert.That(file.PropertyChanges).Count().IsEqualTo(4);
        await ExpectProperty(
            file.PropertyChanges[0],
            "multi",
            PropertyChangeKind.Modified,
            (1, 3, 1, 3),
            Context("first", 1, 1),
            Removed("second", 2),
            Added("SECOND", 2),
            Context("third", 3, 3)
        );
        await ExpectProperty(
            file.PropertyChanges[1],
            "to-add",
            PropertyChangeKind.Added,
            (0, 0, 1, 1),
            Added("hello", 1, endsWithoutNewline: true)
        );
        await ExpectProperty(
            file.PropertyChanges[2],
            "to-delete",
            PropertyChangeKind.Deleted,
            (1, 1, 0, 0),
            Removed("bye", 1, endsWithoutNewline: true)
        );
        await ExpectProperty(
            file.PropertyChanges[3],
            "to-modify",
            PropertyChangeKind.Modified,
            (1, 1, 1, 1),
            Removed("old value", 1, endsWithoutNewline: true),
            Added("new value", 1, endsWithoutNewline: true)
        );
    }

    [Test]
    public async Task Properties_on_a_directory_are_read_like_a_files()
    {
        var file = CapturedDiff.OnlyFile("propdir.diff");

        await Assert.That(file.Path).IsEqualTo("propdir");
        await Assert.That(file.Content).IsNull();
        await Assert.That(file.PropertyChanges).Count().IsEqualTo(3);
        await ExpectProperty(
            file.PropertyChanges[0],
            "dir-add",
            PropertyChangeKind.Added,
            (0, 0, 1, 1),
            Added("y", 1, endsWithoutNewline: true)
        );
        await ExpectProperty(
            file.PropertyChanges[1],
            "dir-delete",
            PropertyChangeKind.Deleted,
            (1, 1, 0, 0),
            Removed("x", 1, endsWithoutNewline: true)
        );
        await ExpectProperty(
            file.PropertyChanges[2],
            "svn:ignore",
            PropertyChangeKind.Modified,
            (1, 1, 1, 2),
            Context("*.tmp", 1, 1),
            Added("*.bak", 2)
        );
    }

    [Test]
    public async Task A_content_change_and_a_property_change_on_one_file_are_both_kept()
    {
        var file = CapturedDiff.FileIn("root.diff", "textprops.txt");

        await ExpectedHunk.IsOnlyHunkOf(
            file.Content,
            (1, 1, 1, 1),
            Removed("text and props", 1),
            Added("text and props changed", 1)
        );
        await Assert.That(file.PropertyChanges).Count().IsEqualTo(1);
        await ExpectProperty(
            file.PropertyChanges[0],
            "svn:mime-type",
            PropertyChangeKind.Added,
            (0, 0, 1, 1),
            Added("text/plain", 1, endsWithoutNewline: true)
        );
    }

    [Test]
    public async Task An_added_empty_file_with_a_property_reads_as_a_property_only_change()
    {
        var file = CapturedDiff.OnlyFile("empty-props.diff");

        await Assert.That(file.Path).IsEqualTo("empty-props.txt");
        await Assert.That(file.Content).IsNull();
        await Assert.That(file.PropertyChanges).Count().IsEqualTo(1);
        await ExpectProperty(
            file.PropertyChanges[0],
            "flag",
            PropertyChangeKind.Added,
            (0, 0, 1, 1),
            Added("on", 1, endsWithoutNewline: true)
        );
    }

    [Test]
    public async Task A_binary_file_with_a_property_change_is_one_file_though_svn_prints_two_sections_for_it()
    {
        await Assert
            .That(CapturedDiff.Read("bin-mod-props.diff").Split("Index: "))
            .Count()
            .IsEqualTo(3);

        var file = CapturedDiff.OnlyFile("bin-mod-props.diff");

        await Assert.That(file.Path).IsEqualTo("bin-mod-props.bin");
        await Assert.That(file.Content).IsEqualTo(new BinaryChange("application/octet-stream"));
        await Assert.That(file.PropertyChanges).Count().IsEqualTo(1);
        await ExpectProperty(
            file.PropertyChanges[0],
            "extra",
            PropertyChangeKind.Added,
            (0, 0, 1, 1),
            Added("e", 1, endsWithoutNewline: true)
        );
    }

    [Test]
    public async Task An_added_binary_file_carries_the_mime_type_svn_add_gave_it_as_a_property()
    {
        var file = CapturedDiff.OnlyFile("bin-add.diff");

        await Assert.That(file.Path).IsEqualTo("bin-add.png");
        await Assert.That(file.Content).IsEqualTo(new BinaryChange("application/octet-stream"));
        await Assert.That(file.PropertyChanges).Count().IsEqualTo(1);
        await ExpectProperty(
            file.PropertyChanges[0],
            "svn:mime-type",
            PropertyChangeKind.Added,
            (0, 0, 1, 1),
            Added("application/octet-stream", 1, endsWithoutNewline: true)
        );
    }

    [Test]
    public async Task Property_value_lines_shaped_like_headers_stay_in_the_hunk()
    {
        var file = CapturedDiff.OnlyFile("trickyprop.diff");

        await Assert.That(file.Content).IsNull();
        await Assert.That(file.PropertyChanges).Count().IsEqualTo(1);
        await ExpectProperty(
            file.PropertyChanges[0],
            "tricky",
            PropertyChangeKind.Modified,
            (1, 2, 1, 3),
            Context("Added: x", 1, 1),
            Context("## -1 +1 ##", 2, 2),
            Added("Index: y", 3)
        );
    }

    [Test]
    public async Task Added_mergeinfo_is_the_merge_summary_svn_prints_in_place_of_a_hunk()
    {
        var file = CapturedDiff.OnlyFile("mergedir.diff");

        await Assert.That(file.Content).IsNull();
        await Assert.That(file.PropertyChanges).Count().IsEqualTo(1);
        var change = file.PropertyChanges[0];
        await Assert.That(change.Name).IsEqualTo("svn:mergeinfo");
        await Assert.That(change.Kind).IsEqualTo(PropertyChangeKind.Added);
        await Assert.That(change.Hunks).IsEmpty();
        await Assert.That(change.MergeSummary).IsEquivalentTo(["Merged /branches/feature:r2"]);
    }

    [Test]
    public async Task Modified_mergeinfo_keeps_every_merged_and_reverse_merged_line_in_order()
    {
        var change = CapturedDiff.OnlyFile("mergemod.diff").PropertyChanges.Single();

        await Assert.That(change.Name).IsEqualTo("svn:mergeinfo");
        await Assert.That(change.Kind).IsEqualTo(PropertyChangeKind.Modified);
        await Assert.That(change.Hunks).IsEmpty();
        await Assert
            .That(change.MergeSummary)
            .IsEquivalentTo(
                [
                    "Reverse-merged /branches/gone:r3",
                    "Merged /branches/a:r3-4",
                    "Merged /branches/b:r5",
                ],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task A_property_diffed_in_hunks_has_no_merge_summary()
    {
        var change = CapturedDiff.FileIn("root.diff", "propfile.txt").PropertyChanges[0];

        await Assert.That(change.MergeSummary).IsEmpty();
    }

    [Test]
    public async Task A_property_on_the_working_copy_root_belongs_to_the_empty_path()
    {
        var root = CapturedDiff.Parse("root.diff").Files[^1];

        await Assert.That(root.Path).IsEqualTo(string.Empty);
        await Assert.That(root.Content).IsNull();
        await Assert.That(root.PropertyChanges).Count().IsEqualTo(1);
        await ExpectProperty(
            root.PropertyChanges[0],
            "root-prop",
            PropertyChangeKind.Added,
            (0, 0, 1, 1),
            Added("r", 1, endsWithoutNewline: true)
        );
    }

    [Test]
    public async Task A_property_conflict_diffs_the_working_value_against_base()
    {
        var file = CapturedDiff.FileIn("props-fixture.diff", "clean.txt");

        await Assert.That(file.Content).IsNull();
        await Assert.That(file.PropertyChanges).Count().IsEqualTo(1);
        await ExpectProperty(
            file.PropertyChanges[0],
            "svn:mime-type",
            PropertyChangeKind.Modified,
            (1, 1, 1, 1),
            Removed("application/octet-stream", 1, endsWithoutNewline: true),
            Added("text/x-local", 1, endsWithoutNewline: true)
        );
    }

    [Test]
    public async Task The_props_fixture_reads_as_one_file_per_section_with_its_directory_last()
    {
        var document = CapturedDiff.Parse("props-fixture.diff");

        await Assert
            .That(document.Files.Select(file => file.Path))
            .IsEquivalentTo(
                [
                    "added.txt",
                    "both.txt",
                    "clean.txt",
                    "copied-propmod.txt",
                    "propdel.txt",
                    "propmod.txt",
                    "sub/child.txt",
                    "sub",
                ],
                CollectionOrdering.Matching
            );
        await ExpectProperty(
            document.Files[^1].PropertyChanges.Single(),
            "svn:ignore",
            PropertyChangeKind.Modified,
            (1, 1, 1, 1),
            Removed("build", 1),
            Added("dist", 1)
        );
    }

    private static async Task ExpectProperty(
        PropertyChange actual,
        string name,
        PropertyChangeKind kind,
        (int OldStart, int OldCount, int NewStart, int NewCount) header,
        params DiffLine[] lines
    )
    {
        await Assert.That(actual.Name).IsEqualTo(name);
        await Assert.That(actual.Kind).IsEqualTo(kind);
        await Assert.That(actual.Hunks).Count().IsEqualTo(1);
        await ExpectedHunk.Matches(actual.Hunks[0], header, lines);
    }
}
