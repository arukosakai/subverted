using Subverted.App.Presentation;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

public sealed class ChangeRowTests
{
    [Test]
    [Arguments("art/ui/button.png", "button.png", "art/ui")]
    [Arguments("readme.txt", "readme.txt", "")]
    [Arguments("", ".", "")]
    public async Task A_path_splits_into_what_to_look_for_and_where_it_is(
        string relPath,
        string name,
        string folder
    )
    {
        var row = ChangeRow.From(Entry(relPath));

        await Assert.That(row.Name).IsEqualTo(name);
        await Assert.That(row.Folder).IsEqualTo(folder);
        await Assert.That(row.RelPath).IsEqualTo(relPath);
    }

    /// <summary>
    /// The tag is for the node whose badge cannot say both: modified content and properties. With
    /// clean content the badge already says "Properties", and a tag as well would say it twice.
    /// </summary>
    [Test]
    [Arguments(NodeStatus.Modified, PropertyStatus.Modified, true)]
    [Arguments(NodeStatus.Unmodified, PropertyStatus.Modified, false)]
    [Arguments(NodeStatus.Modified, PropertyStatus.Unmodified, false)]
    public async Task The_properties_tag_appears_only_beside_a_content_change(
        NodeStatus status,
        PropertyStatus properties,
        bool expected
    )
    {
        await Assert
            .That(ChangeRow.From(Entry("a.png", status, properties)).HasPropertyChange)
            .IsEqualTo(expected);
    }

    /// <summary>
    /// Checked against <c>svn log</c> on <c>subverted-copy</c>: a plain add answers E195002 and an
    /// unversioned node E155010, while a copy, a delete and an edit all have revisions to show.
    /// </summary>
    [Test]
    [Arguments(NodeStatus.Added, false, false)]
    [Arguments(NodeStatus.Added, true, true)]
    [Arguments(NodeStatus.Unversioned, false, false)]
    [Arguments(NodeStatus.Ignored, false, false)]
    [Arguments(NodeStatus.Modified, false, true)]
    [Arguments(NodeStatus.Deleted, false, true)]
    [Arguments(NodeStatus.Missing, false, true)]
    public async Task Only_a_node_that_was_ever_committed_has_history(
        NodeStatus status,
        bool isCopied,
        bool expected
    )
    {
        await Assert
            .That(ChangeRow.From(Entry("a.png", status, isCopied: isCopied)).HasHistory)
            .IsEqualTo(expected);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task A_row_carries_the_copy_and_the_lock_as_they_are(bool flag)
    {
        var row = ChangeRow.From(Entry("a.png", isCopied: flag, hasLockToken: !flag));

        await Assert.That(row.IsCopied).IsEqualTo(flag);
        await Assert.That(row.IsLocked).IsEqualTo(!flag);
    }

    [Test]
    public async Task A_row_carries_the_fingerprint_it_was_listed_with()
    {
        var onDisk = new FileFingerprint(10, new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        await Assert.That(ChangeRow.From(Entry("a.png", onDisk: onDisk)).OnDisk).IsEqualTo(onDisk);
        await Assert.That(ChangeRow.From(Entry("a.png")).OnDisk).IsNull();
    }
}
