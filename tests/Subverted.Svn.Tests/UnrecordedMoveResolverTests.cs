namespace Subverted.Svn.Tests;

public sealed class UnrecordedMoveResolverTests
{
    private const string DigestA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DigestB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string DigestC = "cccccccccccccccccccccccccccccccccccccccc";

    [Test]
    public async Task A_missing_node_pairs_with_the_unversioned_file_holding_its_content()
    {
        var moves = UnrecordedMoveResolver.Pair(
            [Node("art/hero.png", DigestA)],
            [Node("art/protagonist.png", DigestA)]
        );

        await Assert.That(moves.Count).IsEqualTo(1);
        await Assert.That(moves[0].FromRelPath).IsEqualTo("art/hero.png");
        await Assert.That(moves[0].ToRelPath).IsEqualTo("art/protagonist.png");
    }

    /// <summary>
    /// The negative case the pairing exists to get right: a file that went missing and an unrelated
    /// new file are the ordinary state of a working copy, and calling that a rename would be worse
    /// than saying nothing.
    /// </summary>
    [Test]
    public async Task Content_that_does_not_match_is_not_a_move()
    {
        var moves = UnrecordedMoveResolver.Pair(
            [Node("art/hero.png", DigestA)],
            [Node("art/unrelated.png", DigestB)]
        );

        await Assert.That(moves).IsEmpty();
    }

    [Test]
    public async Task Two_missing_nodes_sharing_content_pair_with_nothing()
    {
        var moves = UnrecordedMoveResolver.Pair(
            [Node("art/one.png", DigestA), Node("art/two.png", DigestA)],
            [Node("art/somewhere.png", DigestA)]
        );

        await Assert.That(moves).IsEmpty();
    }

    [Test]
    public async Task Two_unversioned_files_sharing_content_pair_with_nothing()
    {
        var moves = UnrecordedMoveResolver.Pair(
            [Node("art/hero.png", DigestA)],
            [Node("art/copy-one.png", DigestA), Node("art/copy-two.png", DigestA)]
        );

        await Assert.That(moves).IsEmpty();
    }

    /// <summary>
    /// Ambiguity is per digest, not per call: duplicate content in one corner of a working copy must
    /// not stop an unrelated rename elsewhere being reported.
    /// </summary>
    [Test]
    public async Task An_ambiguous_digest_does_not_suppress_an_unambiguous_one()
    {
        var moves = UnrecordedMoveResolver.Pair(
            [
                Node("art/one.png", DigestA),
                Node("art/two.png", DigestA),
                Node("doc/spec.md", DigestB),
            ],
            [Node("art/where.png", DigestA), Node("doc/specification.md", DigestB)]
        );

        await Assert.That(moves.Count).IsEqualTo(1);
        await Assert.That(moves[0].FromRelPath).IsEqualTo("doc/spec.md");
        await Assert.That(moves[0].ToRelPath).IsEqualTo("doc/specification.md");
    }

    [Test]
    public async Task A_missing_node_with_no_match_is_left_out_while_another_pairs()
    {
        var moves = UnrecordedMoveResolver.Pair(
            [Node("art/hero.png", DigestA), Node("art/gone.png", DigestC)],
            [Node("art/protagonist.png", DigestA)]
        );

        await Assert.That(moves.Count).IsEqualTo(1);
        await Assert.That(moves[0].FromRelPath).IsEqualTo("art/hero.png");
    }

    [Test]
    public async Task Pairs_are_ordered_by_the_path_that_went_missing()
    {
        var moves = UnrecordedMoveResolver.Pair(
            [Node("z/last.png", DigestA), Node("a/first.png", DigestB)],
            [Node("moved/one.png", DigestA), Node("moved/two.png", DigestB)]
        );

        await Assert
            .That(moves.Select(move => move.FromRelPath))
            .IsEquivalentTo(new[] { "a/first.png", "z/last.png" });
    }

    [Test]
    public async Task Nothing_missing_is_nothing_to_pair()
    {
        await Assert.That(UnrecordedMoveResolver.Pair([], [Node("a.png", DigestA)])).IsEmpty();
    }

    [Test]
    public async Task Nothing_unversioned_is_nothing_to_pair_with()
    {
        await Assert.That(UnrecordedMoveResolver.Pair([Node("a.png", DigestA)], [])).IsEmpty();
    }

    private static FingerprintedNode Node(string relPath, string digest) => new(relPath, digest);
}
