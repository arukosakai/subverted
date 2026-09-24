using Subverted.App.Presentation;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>Which rows the table and the tree are drawn from, for each side of the Changed / All toggle.</summary>
public sealed class ListedLinesTests
{
    private static readonly ChangeRow Edited = ChangeRow.From(Entry("art/hero.png"));
    private static readonly ChangeRow Untouched = ChangeRow.From(
        Entry("art/tree.png", NodeStatus.Unmodified)
    );
    private static readonly ChangeRow UntouchedFolder = ChangeRow.From(
        Entry("art", NodeStatus.Unmodified, kind: NodeKind.Directory)
    );
    private static readonly ChangeRow Held = ChangeRow.From(
        Entry("art/held.psd", NodeStatus.Unmodified, hasLockToken: true)
    );
    private static readonly ChangeRow AddedFolder = ChangeRow.From(
        Entry("fx", NodeStatus.Added, kind: NodeKind.Directory)
    );

    private static readonly IReadOnlyList<ChangeRow> Everything =
    [
        UntouchedFolder,
        Held,
        Edited,
        Untouched,
        AddedFolder,
    ];

    /// <summary>
    /// A listing asked for all can still be on hand when the toggle goes back, so Changes filters
    /// it rather than trusting that the daemon did.
    /// </summary>
    [Test]
    public async Task Changes_keeps_only_rows_with_something_to_report_in_the_order_given()
    {
        var lines = ListedLines.Of(Everything, ListedNodes.Changes);

        await Assert
            .That(string.Join(",", lines.Select(row => row.RelPath)))
            .IsEqualTo("art/held.psd,art/hero.png,fx");
    }

    [Test]
    public async Task All_adds_the_unmodified_files_but_not_the_unmodified_folders()
    {
        var lines = ListedLines.Of(Everything, ListedNodes.All);

        await Assert
            .That(string.Join(",", lines.Select(row => row.RelPath)))
            .IsEqualTo("art/held.psd,art/hero.png,art/tree.png,fx");
    }

    [Test]
    public async Task A_row_is_unmodified_exactly_when_it_has_nothing_to_report()
    {
        await Assert.That(Untouched.IsUnmodified).IsTrue();
        await Assert.That(Held.IsUnmodified).IsFalse();
        await Assert.That(Edited.IsUnmodified).IsFalse();
    }
}
