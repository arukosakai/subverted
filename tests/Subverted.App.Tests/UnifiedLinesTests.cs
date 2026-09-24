using Subverted.App.Presentation;
using Subverted.Frontend.Diff;
using TUnit.Assertions.Enums;
using static Subverted.App.Tests.Diffs;

namespace Subverted.App.Tests;

public sealed class UnifiedLinesTests
{
    [Test]
    public async Task No_lines_make_no_rows()
    {
        await Assert.That(UnifiedLines.Of([])).IsEmpty();
    }

    [Test]
    public async Task Context_lines_have_no_counterpart()
    {
        var first = Context(1, 1, "a");
        var second = Context(2, 2, "b");

        await Assert
            .That(UnifiedLines.Of([first, second]))
            .IsEquivalentTo(
                [new DiffTextRow(first), new DiffTextRow(second)],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task A_replaced_line_and_its_replacement_name_each_other()
    {
        var old = Removed(3, "trees 40");
        var @new = Added(3, "trees 55");

        await Assert
            .That(UnifiedLines.Of([old, @new]))
            .IsEquivalentTo(
                [new DiffTextRow(old, @new), new DiffTextRow(@new, old)],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task Lines_pair_in_order_and_the_longer_side_runs_out_unpaired()
    {
        var oldA = Removed(1, "a");
        var oldB = Removed(2, "b");
        var oldC = Removed(3, "c");
        var newA = Added(1, "A");
        var newB = Added(2, "B");

        await Assert
            .That(UnifiedLines.Of([oldA, oldB, oldC, newA, newB]))
            .IsEquivalentTo(
                [
                    new DiffTextRow(oldA, newA),
                    new DiffTextRow(oldB, newB),
                    new DiffTextRow(oldC),
                    new DiffTextRow(newA, oldA),
                    new DiffTextRow(newB, oldB),
                ],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task More_added_than_removed_leaves_the_extra_added_lines_unpaired()
    {
        var old = Removed(13, "position += velocity;");
        var first = Added(13, "position += velocity * delta;");
        var second = Added(14, "ClampToLevel();");

        await Assert
            .That(UnifiedLines.Of([old, first, second]))
            .IsEquivalentTo(
                [new DiffTextRow(old, first), new DiffTextRow(first, old), new DiffTextRow(second)],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task Context_ends_a_run_so_lines_either_side_of_it_do_not_pair()
    {
        var old = Removed(1, "a");
        var kept = Context(2, 1, "kept");
        var @new = Added(2, "b");

        await Assert
            .That(UnifiedLines.Of([old, kept, @new]))
            .IsEquivalentTo(
                [new DiffTextRow(old), new DiffTextRow(kept), new DiffTextRow(@new)],
                CollectionOrdering.Matching
            );
    }

    /// <summary>A kind the parser never produces is laid out as context, as <see cref="SplitLines"/> does.</summary>
    [Test]
    public async Task An_unknown_kind_of_line_ends_a_run_like_context()
    {
        var old = Removed(1, "a");
        var odd = new DiffLine((DiffLineKind)99, "odd", 1, 1, EndsWithoutNewline: false);
        var @new = Added(2, "b");

        await Assert
            .That(UnifiedLines.Of([old, odd, @new]))
            .IsEquivalentTo(
                [new DiffTextRow(old), new DiffTextRow(odd), new DiffTextRow(@new)],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task Each_run_pairs_on_its_own()
    {
        var firstOld = Removed(1, "a");
        var firstNew = Added(1, "A");
        var kept = Context(2, 2, "kept");
        var secondOld = Removed(3, "b");
        var secondNew = Added(3, "B");

        await Assert
            .That(UnifiedLines.Of([firstOld, firstNew, kept, secondOld, secondNew]))
            .IsEquivalentTo(
                [
                    new DiffTextRow(firstOld, firstNew),
                    new DiffTextRow(firstNew, firstOld),
                    new DiffTextRow(kept),
                    new DiffTextRow(secondOld, secondNew),
                    new DiffTextRow(secondNew, secondOld),
                ],
                CollectionOrdering.Matching
            );
    }
}
