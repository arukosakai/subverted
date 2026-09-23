using Subverted.App.Presentation;
using Subverted.App.ViewModels;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>Whether a status refresh warrants asking for the shown diff again.</summary>
public sealed class DiffFreshnessTests
{
    [Test]
    public async Task An_equal_row_in_the_new_listing_needs_no_refetch()
    {
        var shown = ChangeRow.From(Entry("a.png"));

        await Assert
            .That(DiffFreshness.NeedsRefetch(shown, ChangeRow.From(Entry("a.png"))))
            .IsFalse();
    }

    [Test]
    [Arguments(NodeStatus.Modified, PropertyStatus.Modified, false)]
    [Arguments(NodeStatus.Conflicted, PropertyStatus.Unmodified, false)]
    [Arguments(NodeStatus.Modified, PropertyStatus.Unmodified, true)]
    public async Task A_row_whose_record_changed_needs_a_refetch(
        NodeStatus status,
        PropertyStatus properties,
        bool locked
    )
    {
        var shown = ChangeRow.From(Entry("a.png"));
        var listed = ChangeRow.From(Entry("a.png", status, properties, hasLockToken: locked));

        await Assert.That(DiffFreshness.NeedsRefetch(shown, listed)).IsTrue();
    }
}
