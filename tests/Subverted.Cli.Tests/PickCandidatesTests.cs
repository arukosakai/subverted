using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Cli.Tests;

/// <summary>
/// What the picker is allowed to offer. Two things ride on it: offering a node <c>svn commit</c>
/// refuses fails the whole commit, and the order decides whether a directory is answered before the
/// children its answer settles.
/// </summary>
public sealed class PickCandidatesTests
{
    private const StringComparison Sensitive = StringComparison.Ordinal;

    private static readonly string Root = Path.GetFullPath("/wc");

    [Test]
    [Arguments(NodeStatus.Modified)]
    [Arguments(NodeStatus.Added)]
    [Arguments(NodeStatus.Deleted)]
    [Arguments(NodeStatus.Replaced)]
    [Arguments(NodeStatus.NeedsPristineCompare)]
    public async Task A_node_svn_can_commit_is_offered(NodeStatus status)
    {
        var listing = Status(Modified("src/a.txt") with { Status = status });

        await Assert.That(Under(listing).Select(entry => entry.RelPath)).Contains("src/a.txt");
    }

    /// <summary>
    /// The negative half of the case above, and the one that matters more: naming any of these as a
    /// commit target fails the commit outright or asks the server for something it cannot do.
    /// </summary>
    [Test]
    [Arguments(NodeStatus.Unmodified)]
    [Arguments(NodeStatus.Unversioned)]
    [Arguments(NodeStatus.Ignored)]
    [Arguments(NodeStatus.External)]
    [Arguments(NodeStatus.Missing)]
    [Arguments(NodeStatus.Obstructed)]
    [Arguments(NodeStatus.Incomplete)]
    [Arguments(NodeStatus.Conflicted)]
    public async Task A_node_svn_cannot_commit_as_it_stands_is_not_offered(NodeStatus status)
    {
        var listing = Status(Modified("src/a.txt") with { Status = status });

        await Assert.That(Under(listing)).IsEmpty();
    }

    /// <summary>
    /// A node in conflict is refused by <c>svn commit</c> however its content axis reads, so the
    /// content status alone is not enough to decide this.
    /// </summary>
    [Test]
    public async Task A_conflicted_node_is_not_offered_even_when_its_content_reads_modified()
    {
        var listing = Status(Modified("src/a.txt") with { IsConflicted = true });

        await Assert.That(Under(listing)).IsEmpty();
    }

    /// <summary>
    /// SVN's second column. A property-only change leaves the content axis reading clean, and a
    /// picker that trusted that axis alone would never let anyone commit one.
    /// </summary>
    [Test]
    public async Task A_property_only_change_is_offered_although_its_content_is_unmodified()
    {
        var listing = Status(PropertiesChanged("src"));

        await Assert.That(Under(listing).Select(entry => entry.RelPath)).IsEquivalentTo(["src"]);
    }

    [Test]
    public async Task Only_what_is_under_the_named_path_is_offered()
    {
        var listing = Status(Modified("art/hero.png"), Modified("src/a.txt"));

        var candidates = PickCandidates.Under(listing, Path.Combine(Root, "art"), Sensitive);

        await Assert
            .That(candidates.Select(entry => entry.RelPath))
            .IsEquivalentTo(["art/hero.png"]);
    }

    /// <summary>
    /// Ancestors first, because every directory rule in <see cref="ChangePicker"/> depends on the
    /// directory having been answered before the nodes its answer settles. Ordinal order gives it
    /// for free — <c>/</c> sorts below every character a name can start with.
    /// </summary>
    [Test]
    public async Task A_directory_is_offered_before_anything_under_it_however_the_daemon_listed_them()
    {
        var listing = Status(
            Modified("src/a.txt"),
            Modified("srcfile.txt"),
            PropertiesChanged("src"),
            Modified("src/deep/d.txt"),
            PropertiesChanged("src/deep")
        );

        var candidates = PickCandidates.Under(listing, Root, Sensitive);

        await Assert
            .That(candidates.Select(entry => entry.RelPath))
            .IsEquivalentTo(["src", "src/a.txt", "src/deep", "src/deep/d.txt", "srcfile.txt"]);
    }

    [Test]
    public async Task Nothing_committable_under_the_path_is_an_empty_list()
    {
        var listing = Status(Modified("art/hero.png"));

        await Assert
            .That(PickCandidates.Under(listing, Path.Combine(Root, "src"), Sensitive))
            .IsEmpty();
    }

    [Test]
    [Arguments(StringComparison.OrdinalIgnoreCase, 1)]
    [Arguments(StringComparison.Ordinal, 0)]
    public async Task Case_is_the_platforms_business_and_not_this_functions(
        StringComparison comparison,
        int expected
    )
    {
        var listing = Status(Modified("Art/hero.png"));

        var candidates = PickCandidates.Under(listing, Path.Combine(Root, "art"), comparison);

        await Assert.That(candidates.Count).IsEqualTo(expected);
    }

    private static IReadOnlyList<WorkingCopyEntry> Under(StatusResponse listing) =>
        PickCandidates.Under(listing, Root, Sensitive);

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

    private static WorkingCopyEntry PropertiesChanged(string relPath) =>
        Modified(relPath) with
        {
            Kind = NodeKind.Directory,
            Status = NodeStatus.Unmodified,
            PropertyStatus = PropertyStatus.Modified,
        };
}
