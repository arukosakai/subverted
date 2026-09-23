using Subverted.App.Presentation;
using Subverted.Core;

namespace Subverted.App.Tests;

public sealed class BaseMarkerTests
{
    [Test]
    public async Task With_base_unknown_no_row_is_marked_either_way()
    {
        var items = BaseMarker.Place(Rows(12, 9), range: null);

        await Assert
            .That(items.Select(item => item.Presence))
            .IsEquivalentTo([null, (RevisionPresence?)null]);
        await Assert
            .That(items.Select(item => item.BaseMarkerAbove))
            .IsEquivalentTo([null, (string?)null]);
    }

    [Test]
    public async Task The_line_sits_above_the_newest_revision_the_copy_has()
    {
        var items = BaseMarker.Place(Rows(12, 10, 9, 7), new BaseRevisionRange(9, 9));

        await Assert
            .That(items.Select(item => item.BaseMarkerAbove))
            .IsEquivalentTo(
                [null, null, "Your copy is at r9", null],
                TUnit.Assertions.Enums.CollectionOrdering.Matching
            );
        await Assert
            .That(items.Select(item => item.Presence))
            .IsEquivalentTo(
                [
                    RevisionPresence.NotInCopy,
                    RevisionPresence.NotInCopy,
                    RevisionPresence.InCopy,
                    (RevisionPresence?)RevisionPresence.InCopy,
                ],
                TUnit.Assertions.Enums.CollectionOrdering.Matching
            );
    }

    /// <summary>
    /// BASE past the path's last change: the copy has everything listed, so the line is at the top.
    /// </summary>
    [Test]
    public async Task A_copy_with_everything_shown_has_the_line_above_the_first_row()
    {
        var items = BaseMarker.Place(Rows(7, 5), new BaseRevisionRange(9, 9));

        await Assert.That(items[0].BaseMarkerAbove).IsEqualTo("Your copy is at r9");
        await Assert.That(items[1].BaseMarkerAbove).IsNull();
    }

    [Test]
    public async Task A_copy_older_than_everything_shown_has_no_line_yet()
    {
        var items = BaseMarker.Place(Rows(12, 10), new BaseRevisionRange(9, 9));

        await Assert.That(items.Where(item => item.BaseMarkerAbove is not null)).IsEmpty();
        await Assert.That(items.All(item => item.IsNotInCopy)).IsTrue();
    }

    [Test]
    public async Task A_mixed_copy_has_one_line_above_its_highest_base_and_says_it_is_mixed()
    {
        var items = BaseMarker.Place(Rows(6, 5, 4, 3, 2), new BaseRevisionRange(3, 5));

        await Assert
            .That(items.Select(item => item.BaseMarkerAbove))
            .IsEquivalentTo(
                [null, "Your copy is at r3–r5 — mixed, updated in parts", null, null, null],
                TUnit.Assertions.Enums.CollectionOrdering.Matching
            );
        await Assert
            .That(items.Select(item => item.Presence))
            .IsEquivalentTo(
                [
                    RevisionPresence.NotInCopy,
                    RevisionPresence.PartlyInCopy,
                    RevisionPresence.PartlyInCopy,
                    RevisionPresence.InCopy,
                    (RevisionPresence?)RevisionPresence.InCopy,
                ],
                TUnit.Assertions.Enums.CollectionOrdering.Matching
            );
    }

    [Test]
    [Arguments(6L, RevisionPresence.NotInCopy)]
    [Arguments(5L, RevisionPresence.PartlyInCopy)]
    [Arguments(4L, RevisionPresence.PartlyInCopy)]
    [Arguments(3L, RevisionPresence.InCopy)]
    [Arguments(2L, RevisionPresence.InCopy)]
    public async Task Presence_is_judged_against_both_ends_of_the_range_inclusively(
        long revision,
        RevisionPresence expected
    )
    {
        await Assert
            .That(BaseMarker.PresenceOf(revision, new BaseRevisionRange(3, 5)))
            .IsEqualTo(expected);
    }

    [Test]
    public async Task Only_rows_newer_than_every_base_read_as_not_in_the_copy()
    {
        var items = BaseMarker.Place(Rows(10, 9), new BaseRevisionRange(9, 9));

        await Assert.That(items[0].IsNotInCopy).IsTrue();
        await Assert.That(items[1].IsNotInCopy).IsFalse();
    }

    [Test]
    public async Task Only_rows_between_the_two_ends_of_a_mixed_range_read_as_partly_in_the_copy()
    {
        var items = BaseMarker.Place(Rows(6, 5, 3), new BaseRevisionRange(3, 5));

        await Assert
            .That(items.Select(item => item.IsPartlyInCopy))
            .IsEquivalentTo(
                [false, true, false],
                TUnit.Assertions.Enums.CollectionOrdering.Matching
            );
    }

    private static List<RevisionRow> Rows(params long[] revisions) =>
        [.. revisions.Select(revision => Revisions.Row(revision))];
}
