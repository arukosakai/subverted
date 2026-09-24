using Subverted.App.Presentation;
using Subverted.Frontend.Diff;
using TUnit.Assertions.Enums;
using static Subverted.App.Tests.Diffs;

namespace Subverted.App.Tests;

public sealed class SplitLinesTests
{
    [Test]
    public async Task No_lines_make_no_rows()
    {
        await Assert.That(SplitLines.Of([])).IsEmpty();
    }

    [Test]
    public async Task A_context_line_is_the_same_line_on_both_sides()
    {
        var first = Context(4, 7, "stays");
        var second = Context(5, 8, "stays too");

        await Assert
            .That(SplitLines.Of([first, second]))
            .IsEquivalentTo(
                [new DiffSplitRow(first, first), new DiffSplitRow(second, second)],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task Removed_lines_alone_sit_on_the_left_beside_filler()
    {
        var first = Removed(1, "size 64 64");
        var second = Removed(2, "spawn 3 4");

        await Assert
            .That(SplitLines.Of([first, second]))
            .IsEquivalentTo(
                [new DiffSplitRow(first, null), new DiffSplitRow(second, null)],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task Added_lines_alone_sit_on_the_right_beside_filler()
    {
        var first = Added(1, "namespace Game;");
        var second = Added(2, "");

        await Assert
            .That(SplitLines.Of([first, second]))
            .IsEquivalentTo(
                [new DiffSplitRow(null, first), new DiffSplitRow(null, second)],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task A_replaced_block_of_equal_length_pairs_line_for_line_in_order()
    {
        var oldA = Removed(3, "a");
        var oldB = Removed(4, "b");
        var newA = Added(3, "A");
        var newB = Added(4, "B");

        await Assert
            .That(SplitLines.Of([oldA, oldB, newA, newB]))
            .IsEquivalentTo(
                [new DiffSplitRow(oldA, newA), new DiffSplitRow(oldB, newB)],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task More_removed_than_added_pads_the_right_side_at_the_end_of_the_run()
    {
        var oldA = Removed(3, "a");
        var oldB = Removed(4, "b");
        var oldC = Removed(5, "c");
        var newA = Added(3, "A");

        await Assert
            .That(SplitLines.Of([oldA, oldB, oldC, newA]))
            .IsEquivalentTo(
                [
                    new DiffSplitRow(oldA, newA),
                    new DiffSplitRow(oldB, null),
                    new DiffSplitRow(oldC, null),
                ],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task More_added_than_removed_pads_the_left_side_at_the_end_of_the_run()
    {
        var oldA = Removed(13, "        position += velocity;");
        var newA = Added(13, "        position += velocity * delta;");
        var newB = Added(14, "        ClampToLevel();");

        await Assert
            .That(SplitLines.Of([oldA, newA, newB]))
            .IsEquivalentTo(
                [new DiffSplitRow(oldA, newA), new DiffSplitRow(null, newB)],
                CollectionOrdering.Matching
            );
    }

    /// <summary>SVN prints removals first, but the pairing follows each side's own order either way.</summary>
    [Test]
    public async Task Added_lines_printed_before_removed_ones_still_pair_by_their_order()
    {
        var newA = Added(3, "A");
        var oldA = Removed(3, "a");
        var newB = Added(4, "B");

        await Assert
            .That(SplitLines.Of([newA, oldA, newB]))
            .IsEquivalentTo(
                [new DiffSplitRow(oldA, newA), new DiffSplitRow(null, newB)],
                CollectionOrdering.Matching
            );
    }

    /// <summary>A removal never pairs with an addition from across a context line.</summary>
    [Test]
    public async Task Context_closes_a_change_run_so_runs_either_side_pair_separately()
    {
        var removedAbove = Removed(2, "old two");
        var between = Context(3, 2, "three");
        var removedBelow = Removed(4, "old four");
        var addedBelow = Added(3, "new four");
        var addedAfterIt = Added(4, "new four and a half");
        var last = Context(5, 5, "five");

        await Assert
            .That(
                SplitLines.Of([removedAbove, between, removedBelow, addedBelow, addedAfterIt, last])
            )
            .IsEquivalentTo(
                [
                    new DiffSplitRow(removedAbove, null),
                    new DiffSplitRow(between, between),
                    new DiffSplitRow(removedBelow, addedBelow),
                    new DiffSplitRow(null, addedAfterIt),
                    new DiffSplitRow(last, last),
                ],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task A_run_at_the_very_end_of_the_hunk_is_still_laid_out()
    {
        var before = Context(1, 1, "product=Subverted");
        var oldLast = Removed(2, "version=1.4", endsWithoutNewline: true);
        var newLast = Added(2, "version=1.5", endsWithoutNewline: true);

        await Assert
            .That(SplitLines.Of([before, oldLast, newLast]))
            .IsEquivalentTo(
                [new DiffSplitRow(before, before), new DiffSplitRow(oldLast, newLast)],
                CollectionOrdering.Matching
            );
    }

    /// <summary>The no-newline flag belongs to its own side, so only the side that lost it is marked.</summary>
    [Test]
    public async Task A_missing_final_newline_stays_on_the_side_it_was_printed_for()
    {
        var oldLast = Removed(9, "}", endsWithoutNewline: true);
        var newLast = Added(9, "}");

        var row = SplitLines.Of([oldLast, newLast]).Single();

        await Assert.That(row.Old!.EndsWithoutNewline).IsTrue();
        await Assert.That(row.New!.EndsWithoutNewline).IsFalse();
    }

    [Test]
    public async Task Each_side_keeps_its_own_line_numbers()
    {
        var rows = SplitLines.Of(ModifiedHunk.Lines);

        var numbers = rows.Select(row => (row.Old?.OldNumber, row.New?.NewNumber));

        (int?, int?)[] expected =
        [
            (10, 10),
            (11, 11),
            (12, 12),
            (13, 13),
            (null, 14),
            (14, 15),
            (15, 16),
            (16, 17),
        ];
        await Assert.That(numbers).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    /// <summary>A kind outside the enum is no change, so it is laid out as context rather than dropped.</summary>
    [Test]
    public async Task A_line_of_an_unknown_kind_is_laid_out_as_context()
    {
        var odd = new DiffLine((DiffLineKind)99, "odd", 1, 1, EndsWithoutNewline: false);

        await Assert
            .That(SplitLines.Of([odd]))
            .IsEquivalentTo([new DiffSplitRow(odd, odd)], CollectionOrdering.Matching);
    }
}
