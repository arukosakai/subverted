using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Cli.Tests;

/// <summary>
/// What <c>sv resolve --theirs</c> shows before it asks. Overstating it teaches people to stop
/// reading the list; understating it overwrites a file nobody was warned about.
/// </summary>
public sealed class ConflictPreviewTests
{
    private const StringComparison Sensitive = StringComparison.Ordinal;

    private static readonly string Root = Path.GetFullPath("/wc");

    [Test]
    public async Task A_conflicted_node_under_the_target_is_listed()
    {
        var status = Status(Conflicted("src/a.txt"));

        var lines = ConflictPreview.Lines(status, [Path.Combine(Root, "src")], Sensitive);

        await Assert.That(lines).IsEquivalentTo(["C       src/a.txt"]);
    }

    /// <summary>
    /// The other side of the same rule. A resolve settles conflicts and leaves everything else
    /// alone, so listing a merely modified file claims it is at risk when SVN will not touch it.
    /// </summary>
    [Test]
    public async Task A_modified_node_that_is_not_conflicted_is_not_at_risk()
    {
        var status = Status(Modified("src/a.txt"));

        var lines = ConflictPreview.Lines(status, [Root], Sensitive);

        await Assert.That(lines).IsEmpty();
    }

    /// <summary>
    /// SVN prints a property conflict with a blank first column and both readers fold it onto the
    /// status axis anyway. A resolve settles it like any other, so it belongs in the list — missing
    /// it would tell an artist there was nothing to resolve and then resolve something.
    /// </summary>
    [Test]
    public async Task A_property_conflict_counts_like_any_other()
    {
        var status = Status(PropertyConflicted("src/a.txt"));

        var lines = ConflictPreview.Lines(status, [Root], Sensitive);

        await Assert.That(lines.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Only_the_nodes_under_the_named_target_are_listed()
    {
        var status = Status(Conflicted("art/hero.png"), Conflicted("src/a.txt"));

        var lines = ConflictPreview.Lines(status, [Path.Combine(Root, "art")], Sensitive);

        await Assert.That(lines).IsEquivalentTo(["C       art/hero.png"]);
    }

    [Test]
    public async Task The_target_itself_is_listed_and_not_only_what_is_under_it()
    {
        var status = Status(Conflicted("art/hero.png"));

        var lines = ConflictPreview.Lines(
            status,
            [Path.Combine(Root, "art", "hero.png")],
            Sensitive
        );

        await Assert.That(lines.Count).IsEqualTo(1);
    }

    /// <summary>
    /// <c>art</c> must not claim <c>artefacts</c>, or the count beside the prompt is wrong about
    /// how much is about to be overwritten.
    /// </summary>
    [Test]
    public async Task A_sibling_whose_name_starts_with_the_target_is_not_under_it()
    {
        var status = Status(Conflicted("artefacts/notes.txt"));

        var lines = ConflictPreview.Lines(status, [Path.Combine(Root, "art")], Sensitive);

        await Assert.That(lines).IsEmpty();
    }

    [Test]
    public async Task The_root_covers_every_conflict_in_the_working_copy()
    {
        var status = Status(Conflicted("art/hero.png"), Conflicted("src/a.txt"));

        var lines = ConflictPreview.Lines(status, [Root], Sensitive);

        await Assert.That(lines.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Several_targets_each_contribute_and_a_node_is_listed_once()
    {
        var status = Status(Conflicted("art/hero.png"), Conflicted("src/a.txt"));

        var lines = ConflictPreview.Lines(status, [Root, Path.Combine(Root, "art")], Sensitive);

        await Assert.That(lines.Count).IsEqualTo(2);
    }

    /// <summary>
    /// Case folding is the platform's answer, not ours: on Windows <c>ART</c> and <c>art</c> are
    /// one directory, and on Linux they are two.
    /// </summary>
    [Test]
    [Arguments(StringComparison.OrdinalIgnoreCase, 1)]
    [Arguments(StringComparison.Ordinal, 0)]
    public async Task Whether_a_differently_cased_target_matches_is_the_platforms_answer(
        StringComparison comparison,
        int expected
    )
    {
        var status = Status(Conflicted("art/hero.png"));

        var lines = ConflictPreview.Lines(status, [Path.Combine(Root, "ART")], comparison);

        await Assert.That(lines.Count).IsEqualTo(expected);
    }

    [Test]
    public async Task A_working_copy_with_nothing_conflicted_lists_nothing()
    {
        var status = Status(Modified("src/a.txt"), Modified("art/hero.png"));

        var lines = ConflictPreview.Lines(status, [Root], Sensitive);

        await Assert.That(lines).IsEmpty();
    }

    private static StatusResponse Status(params WorkingCopyEntry[] entries) =>
        new(
            new WorkingCopyInfo(Root, "https://svn.example/repo", "uuid-1", 31),
            entries,
            ServedFromWarmIndex: true,
            ServerElapsedMilliseconds: 0,
            UnfinishedOperations: 0,
            UnrecordedMoves: []
        );

    private static WorkingCopyEntry Modified(string relPath) =>
        new(
            relPath,
            NodeKind.File,
            NodeStatus.Modified,
            PropertyStatus.Unmodified,
            Revision: 1,
            Changelist: null,
            IsConflicted: false,
            HasLockToken: false,
            IsWriteLocked: false,
            IsCopied: false
        );

    private static WorkingCopyEntry Conflicted(string relPath) =>
        Modified(relPath) with
        {
            Status = NodeStatus.Conflicted,
            IsConflicted = true,
        };

    /// <summary>
    /// What both readers produce for a property conflict: the flag set, and the content axis left
    /// saying nothing changed. See <c>SvnStatusXml</c>.
    /// </summary>
    private static WorkingCopyEntry PropertyConflicted(string relPath) =>
        Modified(relPath) with
        {
            Status = NodeStatus.Conflicted,
            PropertyStatus = PropertyStatus.Modified,
            IsConflicted = true,
        };
}
