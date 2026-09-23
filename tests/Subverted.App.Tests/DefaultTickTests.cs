using Subverted.App.Presentation;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

public sealed class DefaultTickTests
{
    /// <summary>
    /// Edits and missing files are the work, so they start ticked; `?` is usually build output, so
    /// it does not. What the commit would refuse or has nothing to send for starts unticked too.
    /// </summary>
    [Test]
    [Arguments(NodeStatus.Modified, true)]
    [Arguments(NodeStatus.Added, true)]
    [Arguments(NodeStatus.Deleted, true)]
    [Arguments(NodeStatus.Replaced, true)]
    [Arguments(NodeStatus.Missing, true)]
    [Arguments(NodeStatus.Unversioned, false)]
    [Arguments(NodeStatus.Ignored, false)]
    [Arguments(NodeStatus.External, false)]
    [Arguments(NodeStatus.Conflicted, false)]
    [Arguments(NodeStatus.Obstructed, false)]
    [Arguments(NodeStatus.Incomplete, false)]
    [Arguments(NodeStatus.NeedsPristineCompare, false)]
    public async Task Each_status_starts_ticked_or_not(NodeStatus status, bool ticked)
    {
        await Assert
            .That(DefaultTick.For(ChangeRow.From(Entry("a.png", status))))
            .IsEqualTo(ticked);
    }

    [Test]
    [Arguments(PropertyStatus.Modified, true)]
    [Arguments(PropertyStatus.Unmodified, false)]
    public async Task A_clean_node_starts_ticked_only_when_its_properties_changed(
        PropertyStatus properties,
        bool ticked
    )
    {
        var row = ChangeRow.From(
            Entry("a.png", NodeStatus.Unmodified, properties, hasLockToken: true)
        );

        await Assert.That(DefaultTick.For(row)).IsEqualTo(ticked);
    }

    /// <summary>A conflict flag outranks the content axis: SVN will not commit it either way.</summary>
    [Test]
    public async Task A_conflicted_edit_starts_unticked()
    {
        var row = ChangeRow.From(Entry("a.png", NodeStatus.Modified, isConflicted: true));

        await Assert.That(DefaultTick.For(row)).IsFalse();
    }

    /// <summary>Its entry is the `?` half, which on its own would start unticked.</summary>
    [Test]
    public async Task A_rename_starts_ticked()
    {
        var row = ChangeRow.Rename(Entry("new.png", NodeStatus.Unversioned), "old.png");

        await Assert.That(DefaultTick.For(row)).IsTrue();
    }
}
