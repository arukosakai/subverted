using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Cli.Tests;

/// <summary>
/// The list someone reads in the second before they lose a morning. Every rule here is one that
/// has to hold in both directions: a node missing from it gets reverted unannounced, and a node
/// wrongly in it teaches people to stop reading the list.
/// </summary>
public sealed class RevertPreviewTests
{
    private const StringComparison Sensitive = StringComparison.Ordinal;

    private static readonly string Root = Path.GetFullPath("/wc");

    [Test]
    public async Task Only_the_nodes_under_the_named_target_are_listed()
    {
        var status = Status(Modified("art/hero.png"), Modified("src/a.txt"));

        var lines = RevertPreview.Lines(status, [Path.Combine(Root, "art")], Sensitive);

        await Assert.That(lines).IsEquivalentTo(["M       art/hero.png"]);
    }

    [Test]
    public async Task The_target_itself_is_listed_and_not_only_what_is_under_it()
    {
        var status = Status(Modified("art/hero.png"));

        var lines = RevertPreview.Lines(status, [Path.Combine(Root, "art", "hero.png")], Sensitive);

        await Assert.That(lines).IsEquivalentTo(["M       art/hero.png"]);
    }

    /// <summary>
    /// The sibling that shares a prefix. <c>art</c> must not claim <c>artefacts</c>, or the prompt
    /// overstates what is at risk and the count beside it is wrong.
    /// </summary>
    [Test]
    public async Task A_sibling_whose_name_starts_with_the_target_is_not_under_it()
    {
        var status = Status(Modified("artefacts/notes.txt"));

        var lines = RevertPreview.Lines(status, [Path.Combine(Root, "art")], Sensitive);

        await Assert.That(lines).IsEmpty();
    }

    [Test]
    public async Task The_root_as_a_target_covers_everything_including_the_root_itself()
    {
        var status = Status(Modified("art/hero.png"), PropertiesChanged(string.Empty));

        var lines = RevertPreview.Lines(status, [Root], Sensitive);

        await Assert.That(lines.Count).IsEqualTo(2);
        await Assert.That(lines).Contains(" M      .");
    }

    [Test]
    public async Task Several_targets_are_all_covered_and_each_node_appears_once()
    {
        var status = Status(Modified("art/hero.png"), Modified("src/a.txt"), Modified("doc/r.md"));

        var lines = RevertPreview.Lines(
            status,
            [Path.Combine(Root, "art"), Path.Combine(Root, "src")],
            Sensitive
        );

        await Assert.That(lines).IsEquivalentTo(["M       art/hero.png", "M       src/a.txt"]);
    }

    /// <summary>
    /// Revert restores versioned nodes and leaves the rest alone, so these three are not at risk.
    /// Listing them would be a warning about work that is not going anywhere.
    /// </summary>
    [Test]
    [Arguments(NodeStatus.Unversioned)]
    [Arguments(NodeStatus.Ignored)]
    [Arguments(NodeStatus.External)]
    public async Task A_node_revert_does_not_touch_is_not_listed_as_being_at_risk(NodeStatus status)
    {
        var listing = Status(Modified("art/hero.png") with { Status = status });

        await Assert.That(RevertPreview.Lines(listing, [Root], Sensitive)).IsEmpty();
    }

    [Test]
    [Arguments(NodeStatus.Modified)]
    [Arguments(NodeStatus.Added)]
    [Arguments(NodeStatus.Deleted)]
    [Arguments(NodeStatus.Replaced)]
    [Arguments(NodeStatus.Missing)]
    [Arguments(NodeStatus.Conflicted)]
    [Arguments(NodeStatus.Obstructed)]
    [Arguments(NodeStatus.Incomplete)]
    [Arguments(NodeStatus.NeedsPristineCompare)]
    public async Task A_node_revert_does_touch_is_listed(NodeStatus status)
    {
        var listing = Status(Modified("art/hero.png") with { Status = status });

        await Assert.That(RevertPreview.Lines(listing, [Root], Sensitive)).IsNotEmpty();
    }

    /// <summary>
    /// A node whose properties alone changed reads as <see cref="NodeStatus.Unmodified"/> on the
    /// content axis, and revert restores it. Filtering on content status alone would drop it.
    /// </summary>
    [Test]
    public async Task A_property_only_change_is_still_something_to_lose()
    {
        var status = Status(PropertiesChanged("art/hero.png"));

        var lines = RevertPreview.Lines(status, [Root], Sensitive);

        await Assert.That(lines).IsEquivalentTo([" M      art/hero.png"]);
    }

    [Test]
    public async Task Nothing_changed_under_the_target_is_an_empty_list_and_not_a_blank_line()
    {
        var status = Status(Modified("src/a.txt"));

        await Assert
            .That(RevertPreview.Lines(status, [Path.Combine(Root, "art")], Sensitive))
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
        var status = Status(Modified("Art/hero.png"));

        var lines = RevertPreview.Lines(status, [Path.Combine(Root, "art")], comparison);

        await Assert.That(lines.Count).IsEqualTo(expected);
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

    private static WorkingCopyEntry PropertiesChanged(string relPath) =>
        Modified(relPath) with
        {
            Status = NodeStatus.Unmodified,
            PropertyStatus = PropertyStatus.Modified,
        };
}
