using Subverted.App.Presentation;
using Subverted.Frontend.Diff;
using TUnit.Assertions.Enums;

namespace Subverted.App.Tests;

public sealed class DiffRowTests
{
    [Test]
    [Arguments(DiffLineKind.Added, "+")]
    [Arguments(DiffLineKind.Removed, "−")]
    [Arguments(DiffLineKind.Context, "")]
    [Arguments((DiffLineKind)99, "")]
    public async Task A_line_is_signed_as_a_unified_diff_signs_it(DiffLineKind kind, string sign)
    {
        var row = new DiffTextRow(new DiffLine(kind, "x", 1, 1, EndsWithoutNewline: false));

        await Assert.That(row.Sign).IsEqualTo(sign);
    }

    public static IEnumerable<Func<(DiffSplitRow, bool, string, string)>> SplitRows() =>
        [
            () =>
                (
                    new DiffSplitRow(Diffs.Context(1, 1, "x"), Diffs.Context(1, 1, "x")),
                    true,
                    "",
                    ""
                ),
            () => (new DiffSplitRow(Diffs.Removed(1, "x"), Diffs.Added(1, "y")), false, "−", "+"),
            () => (new DiffSplitRow(Diffs.Removed(1, "x"), null), false, "−", ""),
            () => (new DiffSplitRow(null, Diffs.Added(1, "y")), false, "", "+"),
        ];

    /// <summary>Each side carries only its own sign; a context row carries neither.</summary>
    [Test]
    [MethodDataSource(nameof(SplitRows))]
    public async Task A_split_row_signs_each_side_for_the_change_it_shows(
        DiffSplitRow row,
        bool isContext,
        string oldSign,
        string newSign
    )
    {
        await Assert.That(row.IsContext).IsEqualTo(isContext);
        await Assert.That(row.OldSign).IsEqualTo(oldSign);
        await Assert.That(row.NewSign).IsEqualTo(newSign);
    }

    public static IEnumerable<Func<(DiffTextRow, ChangedSpan[])>> TextRowChanges() =>
        [
            () =>
                (new DiffTextRow(Diffs.Removed(1, "count = 1"), Diffs.Added(1, "count = 22")), [new(8, 1)]),
            () =>
                (new DiffTextRow(Diffs.Added(1, "count = 22"), Diffs.Removed(1, "count = 1")), [new(8, 2)]),
            () => (new DiffTextRow(Diffs.Removed(1, "count = 1")), []),
            () => (new DiffTextRow(Diffs.Added(1, "count = 22")), []),
            () => (new DiffTextRow(Diffs.Context(1, 1, "count = 1")), []),
        ];

    /// <summary>A removed line marks its own side of the pair, an added line its own; unpaired marks nothing.</summary>
    [Test]
    [MethodDataSource(nameof(TextRowChanges))]
    public async Task A_unified_line_marks_what_changed_on_its_own_side_of_its_pair(
        DiffTextRow row,
        ChangedSpan[] changes
    )
    {
        await Assert.That(row.Changes).IsEquivalentTo(changes, CollectionOrdering.Matching);
    }

    public static IEnumerable<Func<(DiffSplitRow, ChangedSpan[], ChangedSpan[])>> SplitRowChanges() =>
        [
            () =>
                (
                    new DiffSplitRow(Diffs.Removed(1, "count = 1"), Diffs.Added(1, "count = 22")),
                    [new(8, 1)],
                    [new(8, 2)]
                ),
            () => (new DiffSplitRow(Diffs.Removed(1, "count = 1"), null), [], []),
            () => (new DiffSplitRow(null, Diffs.Added(1, "count = 22")), [], []),
            () =>
                (
                    new DiffSplitRow(Diffs.Context(1, 1, "count = 1"), Diffs.Context(1, 1, "count = 1")),
                    [],
                    []
                ),
        ];

    [Test]
    [MethodDataSource(nameof(SplitRowChanges))]
    public async Task A_split_row_marks_what_changed_only_when_both_sides_are_changed_lines(
        DiffSplitRow row,
        ChangedSpan[] oldChanges,
        ChangedSpan[] newChanges
    )
    {
        await Assert.That(row.OldChanges).IsEquivalentTo(oldChanges, CollectionOrdering.Matching);
        await Assert.That(row.NewChanges).IsEquivalentTo(newChanges, CollectionOrdering.Matching);
    }

    [Test]
    [Arguments(PropertyChangeKind.Added, "Added", ChangeTone.Added)]
    [Arguments(PropertyChangeKind.Modified, "Modified", ChangeTone.Modified)]
    [Arguments(PropertyChangeKind.Deleted, "Deleted", ChangeTone.Deleted)]
    [Arguments((PropertyChangeKind)99, "99", ChangeTone.Quiet)]
    public async Task A_property_change_is_labelled_and_toned_like_a_file_change(
        PropertyChangeKind kind,
        string label,
        ChangeTone tone
    )
    {
        var row = new DiffPropertyRow("svn:keywords", kind);

        await Assert.That(row.Label).IsEqualTo(label);
        await Assert.That(row.Tone).IsEqualTo(tone);
    }

    [Test]
    [Arguments("image/png", "image/png")]
    [Arguments(null, "No MIME type recorded")]
    public async Task A_binary_card_names_the_mime_type_or_says_there_is_none(
        string? mimeType,
        string text
    )
    {
        await Assert
            .That(new DiffBinaryRow("a.bin", mimeType, IsOnlyFile: true).MimeTypeText)
            .IsEqualTo(text);
    }
}
