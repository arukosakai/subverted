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

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task A_row_carries_the_copy_and_the_lock_as_they_are(bool flag)
    {
        var row = ChangeRow.From(Entry("a.png", isCopied: flag, hasLockToken: !flag));

        await Assert.That(row.IsCopied).IsEqualTo(flag);
        await Assert.That(row.IsLocked).IsEqualTo(!flag);
    }
}
