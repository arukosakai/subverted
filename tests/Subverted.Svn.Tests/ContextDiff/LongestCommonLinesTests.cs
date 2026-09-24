using TUnit.Assertions.Enums;

namespace Subverted.Svn.Tests.ContextDiff;

public sealed class LongestCommonLinesTests
{
    private const long Plenty = long.MaxValue;

    [Test]
    public async Task Two_empty_texts_share_nothing_and_need_nothing()
    {
        await Assert.That(LongestCommonLines.Find([], [], Plenty)).IsEmpty();
    }

    [Test]
    public async Task Equal_texts_are_one_run()
    {
        var runs = LongestCommonLines.Find([1, 2, 3], [1, 2, 3], Plenty);

        await Assert
            .That(runs)
            .IsEquivalentTo([new MatchedRun(0, 0, 3)], CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_changed_middle_leaves_the_prefix_and_the_suffix_as_runs()
    {
        var runs = LongestCommonLines.Find([1, 2, 3, 4], [1, 9, 3, 4], Plenty);

        await Assert
            .That(runs)
            .IsEquivalentTo(
                [new MatchedRun(0, 0, 1), new MatchedRun(2, 2, 2)],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task A_line_only_one_side_has_splits_the_run_it_falls_in()
    {
        var runs = LongestCommonLines.Find([5, 1, 2], [6, 1, 7, 2], Plenty);

        await Assert
            .That(runs)
            .IsEquivalentTo(
                [new MatchedRun(1, 1, 1), new MatchedRun(2, 3, 1)],
                CollectionOrdering.Matching
            );
    }

    /// <summary>svn's own choice between two smallest diffs, captured as <c>ambiguous.txt</c>.</summary>
    [Test]
    public async Task Of_two_equal_alignments_the_one_taking_shared_lines_earliest_wins()
    {
        int[] abc = [1, 2, 3];
        var runs = LongestCommonLines.Find([.. abc, .. abc], [.. abc, 9, .. abc, .. abc], Plenty);

        await Assert
            .That(runs)
            .IsEquivalentTo(
                [new MatchedRun(0, 0, 3), new MatchedRun(3, 4, 3)],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task A_tie_between_an_insertion_and_a_deletion_takes_the_insertion_first()
    {
        var runs = LongestCommonLines.Find([1, 2], [2, 1], Plenty);

        await Assert
            .That(runs)
            .IsEquivalentTo([new MatchedRun(0, 1, 1)], CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_longer_old_side_is_searched_from_its_own_end_of_the_grid()
    {
        var runs = LongestCommonLines.Find([1, 2, 3, 4], [3], Plenty);

        await Assert
            .That(runs)
            .IsEquivalentTo([new MatchedRun(2, 0, 1)], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Shared_trailing_lines_past_the_fifty_kept_are_one_run_after_the_search()
    {
        var tail = Enumerable.Range(100, LongestCommonLines.SuffixLinesKept + 10).ToArray();

        var runs = LongestCommonLines.Find([1, .. tail], [2, .. tail], Plenty);

        await Assert
            .That(runs)
            .IsEquivalentTo([new MatchedRun(1, 1, tail.Length)], CollectionOrdering.Matching);
    }

    /// <summary>The first round of this search takes one step; a budget of exactly that goes on.</summary>
    [Test]
    public async Task A_search_that_has_spent_exactly_its_budget_carries_on_to_the_answer()
    {
        var runs = LongestCommonLines.Find([1, 2, 1, 2], [2, 1, 2, 1], budget: 1);

        await Assert
            .That(runs)
            .IsEquivalentTo([new MatchedRun(0, 1, 3)], CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_search_that_has_spent_more_than_its_budget_gives_up()
    {
        var runs = LongestCommonLines.Find([1, 2, 1, 2], [2, 1, 2, 1], budget: 0);

        await Assert.That(runs).IsNull();
    }
}
