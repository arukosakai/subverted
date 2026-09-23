using Subverted.App.Presentation;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

public sealed class ChangeOrderTests
{
    [Test]
    public async Task Conflicts_come_first_then_missing_then_everything_else_by_path()
    {
        var ordered = ChangeOrder.Of([
            Row("a/edit.cs"),
            Row("z/gone.png", NodeStatus.Missing),
            Row("m/clash.txt", NodeStatus.Conflicted),
            Row("b/new.cs", NodeStatus.Added),
            Row("c/stray.log", NodeStatus.Unversioned),
        ]);

        await Assert
            .That(Paths(ordered))
            .IsEqualTo("m/clash.txt,z/gone.png,a/edit.cs,b/new.cs,c/stray.log");
    }

    /// <summary>A conflict flagged on a node whose content axis says Modified is still a conflict.</summary>
    [Test]
    public async Task A_flagged_conflict_is_pinned_whatever_its_content_says()
    {
        var ordered = ChangeOrder.Of([
            Row("a.txt"),
            ChangeRow.From(Entry("b.txt", NodeStatus.Modified, isConflicted: true)),
        ]);

        await Assert.That(Paths(ordered)).IsEqualTo("b.txt,a.txt");
    }

    [Test]
    public async Task Within_a_rank_the_order_is_ordinal_by_path()
    {
        var ordered = ChangeOrder.Of([
            Row("b.png", NodeStatus.Missing),
            Row("B.png", NodeStatus.Missing),
            Row("a.png", NodeStatus.Missing),
        ]);

        await Assert.That(Paths(ordered)).IsEqualTo("B.png,a.png,b.png");
    }

    /// <summary>Obstructed and incomplete share the missing tone: all three mean the disk is not what SVN expects.</summary>
    [Test]
    [Arguments(NodeStatus.Conflicted, true)]
    [Arguments(NodeStatus.Missing, true)]
    [Arguments(NodeStatus.Obstructed, true)]
    [Arguments(NodeStatus.Incomplete, true)]
    [Arguments(NodeStatus.Modified, false)]
    [Arguments(NodeStatus.Deleted, false)]
    [Arguments(NodeStatus.Unversioned, false)]
    public async Task Only_conflicts_and_missing_nodes_are_pinned(NodeStatus status, bool pinned)
    {
        await Assert.That(ChangeOrder.IsPinned(Row("a", status))).IsEqualTo(pinned);
    }

    private static ChangeRow Row(string relPath, NodeStatus status = NodeStatus.Modified) =>
        ChangeRow.From(Entry(relPath, status));

    private static string Paths(IEnumerable<ChangeRow> rows) =>
        string.Join(",", rows.Select(row => row.RelPath));
}
