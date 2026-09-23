using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Cli.Tests;

/// <summary>
/// What <c>sv commit --mark</c> names. A selection commit takes only what it is given, so a node
/// left out here is a change that silently stays local while the command reports success.
/// </summary>
public sealed class MarkingCommitTargetsTests
{
    private const StringComparison Sensitive = StringComparison.Ordinal;

    private static readonly string Root = Path.GetFullPath("/wc");

    [Test]
    [Arguments(NodeStatus.Modified)]
    [Arguments(NodeStatus.Added)]
    [Arguments(NodeStatus.Deleted)]
    [Arguments(NodeStatus.Replaced)]
    [Arguments(NodeStatus.NeedsPristineCompare)]
    [Arguments(NodeStatus.Unversioned)]
    [Arguments(NodeStatus.Missing)]
    public async Task Every_change_under_the_path_is_named(NodeStatus status)
    {
        var listing = Status(Entry("src/a.txt", status));

        await Assert.That(Under(listing, Root)).IsEquivalentTo(["src/a.txt"]);
    }

    /// <summary>
    /// Plain <c>svn commit</c> fails on these, so naming them keeps that: the daemon refuses the
    /// request with nothing written, instead of <c>--mark</c> quietly committing around them.
    /// </summary>
    [Test]
    [Arguments(NodeStatus.Conflicted)]
    [Arguments(NodeStatus.Obstructed)]
    [Arguments(NodeStatus.Incomplete)]
    public async Task A_node_svn_would_refuse_is_named_so_the_daemon_refuses_the_lot(
        NodeStatus status
    )
    {
        var listing = Status(Entry("src/a.txt", status));

        await Assert.That(Under(listing, Root)).IsEquivalentTo(["src/a.txt"]);
    }

    [Test]
    public async Task A_conflicted_node_is_named_whatever_its_content_reads()
    {
        var listing = Status(
            Entry("src/a.txt", NodeStatus.Unmodified) with
            {
                IsConflicted = true,
            }
        );

        await Assert.That(Under(listing, Root)).IsEquivalentTo(["src/a.txt"]);
    }

    /// <summary>
    /// An external is a working copy of its own, and a recursive commit passes over it; ignored
    /// nodes are not changes at all.
    /// </summary>
    [Test]
    [Arguments(NodeStatus.External)]
    [Arguments(NodeStatus.Ignored)]
    [Arguments(NodeStatus.Unmodified)]
    public async Task What_a_recursive_commit_passes_over_is_not_named(NodeStatus status)
    {
        var listing = Status(Entry("src/a.txt", status));

        await Assert.That(Under(listing, Root)).IsEmpty();
    }

    [Test]
    public async Task A_property_only_change_is_named_although_its_content_is_unmodified()
    {
        var listing = Status(
            Entry("src", NodeStatus.Unmodified) with
            {
                PropertyStatus = PropertyStatus.Modified,
            }
        );

        await Assert.That(Under(listing, Root)).IsEquivalentTo(["src"]);
    }

    /// <summary>
    /// Both halves of a rename are just a <c>!</c> and a <c>?</c> in the listing. Named together,
    /// the daemon pairs them and records the move.
    /// </summary>
    [Test]
    public async Task Both_halves_of_a_rename_under_the_path_are_named()
    {
        var listing = Status(
            Entry("art/hero.png", NodeStatus.Missing),
            Entry("art/protagonist.png", NodeStatus.Unversioned)
        );

        await Assert
            .That(Under(listing, Root))
            .IsEquivalentTo(["art/hero.png", "art/protagonist.png"]);
    }

    [Test]
    public async Task Only_what_is_under_one_of_the_paths_is_named()
    {
        var listing = Status(
            Entry("art/hero.png", NodeStatus.Modified),
            Entry("src/a.txt", NodeStatus.Unversioned),
            Entry("docs/b.txt", NodeStatus.Missing)
        );

        await Assert
            .That(
                MarkingCommitTargets.Under(
                    listing,
                    [Path.Combine(Root, "art"), Path.Combine(Root, "docs")],
                    Sensitive
                )
            )
            .IsEquivalentTo(["art/hero.png", "docs/b.txt"]);
    }

    private static IReadOnlyList<string> Under(StatusResponse listing, string path) =>
        MarkingCommitTargets.Under(listing, [path], Sensitive);

    private static StatusResponse Status(params WorkingCopyEntry[] entries) =>
        new(
            new WorkingCopyInfo(Root, "https://svn.example/repo", "uuid-1", 31),
            entries,
            ServedFromWarmIndex: true,
            ServerElapsedMilliseconds: 0,
            UnfinishedOperations: 0,
            UnrecordedMoves: []
        );

    private static WorkingCopyEntry Entry(string relPath, NodeStatus status) =>
        new(
            relPath,
            NodeKind.File,
            status,
            PropertyStatus.Unmodified,
            Revision: 1,
            Changelist: null,
            IsConflicted: false,
            HasLockToken: false,
            IsWriteLocked: false,
            IsCopied: false
        );
}
