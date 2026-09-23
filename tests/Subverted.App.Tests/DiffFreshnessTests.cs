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

    /// <summary>
    /// The edit the status axis cannot see: a file already <c>M</c> saved again. A tick is the
    /// smallest step NTFS records, so it is the boundary that has to count.
    /// </summary>
    [Test]
    public async Task A_modified_file_written_again_a_tick_later_needs_a_refetch()
    {
        var shown = ChangeRow.From(Entry("a.png", onDisk: new FileFingerprint(10, Saved)));
        var listed = ChangeRow.From(
            Entry("a.png", onDisk: new FileFingerprint(10, Saved.AddTicks(1)))
        );

        await Assert.That(DiffFreshness.NeedsRefetch(shown, listed)).IsTrue();
    }

    [Test]
    public async Task A_modified_file_that_changed_only_its_length_needs_a_refetch()
    {
        var shown = ChangeRow.From(Entry("a.png", onDisk: new FileFingerprint(10, Saved)));
        var listed = ChangeRow.From(Entry("a.png", onDisk: new FileFingerprint(11, Saved)));

        await Assert.That(DiffFreshness.NeedsRefetch(shown, listed)).IsTrue();
    }

    [Test]
    public async Task An_unchanged_fingerprint_needs_no_refetch()
    {
        var shown = ChangeRow.From(Entry("a.png", onDisk: new FileFingerprint(10, Saved)));
        var listed = ChangeRow.From(Entry("a.png", onDisk: new FileFingerprint(10, Saved)));

        await Assert.That(DiffFreshness.NeedsRefetch(shown, listed)).IsFalse();
    }

    private static readonly DateTime Saved = new(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);
}
