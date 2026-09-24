using TUnit.Assertions.Enums;

namespace Subverted.Svn.Tests.ContextDiff;

public sealed class UnifiedHunksTests
{
    [Test]
    public async Task Equal_texts_have_no_hunk()
    {
        var hunks = UnifiedHunks.Group([new MatchedRun(0, 0, 10)], 10, 10, 3);

        await Assert.That(hunks).IsEmpty();
    }

    [Test]
    public async Task A_change_gets_its_context_on_both_sides()
    {
        var hunks = UnifiedHunks.Group(Replacing(line: 10, of: 20), 20, 20, 3);

        await Assert
            .That(hunks)
            .IsEquivalentTo([new UnifiedHunk(7, 7, 7, 7)], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Context_stops_at_the_top_and_the_bottom_of_the_file()
    {
        var atTop = UnifiedHunks.Group(Replacing(line: 1, of: 3), 3, 3, 3);

        await Assert
            .That(atTop)
            .IsEquivalentTo([new UnifiedHunk(0, 3, 0, 3)], CollectionOrdering.Matching);
    }

    /// <summary>Measured at svn's three: five unchanged lines between two changes is one hunk.</summary>
    [Test]
    public async Task Changes_fewer_than_twice_the_context_apart_share_a_hunk()
    {
        var hunks = UnifiedHunks.Group(TwoChanges(apart: 5), 30, 30, 3);

        await Assert
            .That(hunks)
            .IsEquivalentTo([new UnifiedHunk(7, 13, 7, 13)], CollectionOrdering.Matching);
    }

    /// <summary>…and six is two, although their context would just touch.</summary>
    [Test]
    public async Task Changes_twice_the_context_apart_are_hunks_of_their_own()
    {
        var hunks = UnifiedHunks.Group(TwoChanges(apart: 6), 30, 30, 3);

        await Assert
            .That(hunks)
            .IsEquivalentTo(
                [new UnifiedHunk(7, 7, 7, 7), new UnifiedHunk(14, 7, 14, 7)],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task The_whole_file_as_context_is_one_hunk_covering_every_line()
    {
        var hunks = UnifiedHunks.Group(TwoChanges(apart: 12), 30, 30, int.MaxValue);

        await Assert
            .That(hunks)
            .IsEquivalentTo([new UnifiedHunk(0, 30, 0, 30)], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Lines_added_at_the_end_are_a_change_after_the_last_run()
    {
        var hunks = UnifiedHunks.Group([new MatchedRun(0, 0, 5)], 5, 7, 3);

        await Assert
            .That(hunks)
            .IsEquivalentTo([new UnifiedHunk(2, 3, 2, 5)], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Lines_added_at_the_top_are_a_change_before_the_first_run()
    {
        var hunks = UnifiedHunks.Group([new MatchedRun(0, 2, 5)], 5, 7, 3);

        await Assert
            .That(hunks)
            .IsEquivalentTo([new UnifiedHunk(0, 3, 0, 5)], CollectionOrdering.Matching);
    }

    /// <summary>Line <paramref name="line"/> (zero-based) replaced by another, all else equal.</summary>
    private static MatchedRun[] Replacing(int line, int of) =>
        [new MatchedRun(0, 0, line), new MatchedRun(line + 1, line + 1, of - line - 1)];

    /// <summary>Lines 10 and 11 + <paramref name="apart"/> replaced, in a 30-line file.</summary>
    private static MatchedRun[] TwoChanges(int apart) =>
        [
            new MatchedRun(0, 0, 10),
            new MatchedRun(11, 11, apart),
            new MatchedRun(12 + apart, 12 + apart, 30 - 12 - apart),
        ];
}
